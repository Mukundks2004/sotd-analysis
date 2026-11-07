using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Sotd.Importer;

/// <summary>Minimal Spotify Web API client (client-credentials flow; public catalogue data only).</summary>
public class SpotifyClient : ISpotifyClient
{
    readonly HttpClient _http;
    readonly string _clientId;
    readonly string _clientSecret;
    readonly string _market;
    readonly SemaphoreSlim _requests;
    readonly TimeSpan _maxBackoff;
    readonly object _backoffLock = new();
    DateTime _backoffUntil = DateTime.MinValue;
    readonly object _paceLock = new();
    TimeSpan _minInterval;
    readonly TimeSpan _baseInterval;
    DateTime _nextSlot = DateTime.MinValue;
    int _successes;
    static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(2);

    // A song of the day channel reposts the same artists constantly, and one entry searches several phrasings
    // of the same title, so the same query comes round again and again over a multi-thousand message import.
    // Spotify's quota is spent per request, not per distinct question, so repeats are worth keeping.
    readonly ConcurrentDictionary<string, Lazy<Task<List<SpotifyTrack>>>> _searchCache = new();
    readonly ConcurrentDictionary<string, Lazy<Task<List<string>>>> _genreCache = new();
    string? _token;
    DateTime _tokenExpires = DateTime.MinValue;
    readonly SemaphoreSlim _tokenLock = new(1, 1);

    /// <param name="market">
    /// Pinning a market matters: Spotify "track relinking" hands back a different track id for the same
    /// recording depending on the market, so an unpinned importer can store the same song twice.
    /// </param>
    /// <param name="maxConcurrentRequests">
    /// Spotify's quota belongs to the application, not the connection, so firing more requests at once does
    /// not get through any more of them per second: it only buys a longer 429 penalty. The importer prepares
    /// many messages at once, so this keeps that parallelism away from Spotify.
    /// </param>
    /// <param name="maxBackoff">
    /// How long a 429 may ask us to wait before the run gives up instead. Spotify answers a badly over-quota
    /// app with Retry-After values of hours, and sleeping on one of those looks exactly like a hang.
    /// </param>
    /// <param name="minInterval">
    /// The smallest gap between Spotify requests. This starts at a deliberately polite value rather than at
    /// zero: the adaptive pacing below can only discover the real limit by being refused, and every refusal
    /// earns a longer penalty than the last. Pacing from the first request instead means a long import never
    /// has to trip the limit to find it.
    /// </param>
    public SpotifyClient(HttpClient http, string clientId, string clientSecret, string market = "US",
        int maxConcurrentRequests = 2, TimeSpan? maxBackoff = null, TimeSpan? minInterval = null)
    {
        _http = http;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _market = string.IsNullOrWhiteSpace(market) ? "US" : market.Trim().ToUpperInvariant();
        _requests = new SemaphoreSlim(Math.Max(1, maxConcurrentRequests));
        _maxBackoff = maxBackoff ?? TimeSpan.FromMinutes(10);
        _baseInterval = minInterval ?? TimeSpan.FromMilliseconds(150);
        if (_baseInterval < TimeSpan.Zero) _baseInterval = TimeSpan.Zero;
        _minInterval = _baseInterval;
    }

    async Task<string> TokenAsync()
    {
        if (_token != null && DateTime.UtcNow < _tokenExpires) return _token;

        // Several messages are prepared at once, so without this they would all request a token together.
        await _tokenLock.WaitAsync();
        try
        {
            if (_token != null && DateTime.UtcNow < _tokenExpires) return _token;

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));
            req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            _token = doc.RootElement.GetProperty("access_token").GetString()!;
            _tokenExpires = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 60);
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>GET with retry on 429/401. Returns null for 400/404 (not found / bad id).</summary>
    async Task<JsonDocument?> GetJsonAsync(string url)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await WaitOutBackoffAsync();

            TimeSpan? retryAfter = null;
            var unauthorized = false;
            string body;

            await _requests.WaitAsync();
            try
            {
                await PaceAsync();
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
                using var resp = await _http.SendAsync(req);

                if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    retryAfter = RetryAfter(resp);
                    body = "";
                }
                else if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    unauthorized = true;
                    body = "";
                }
                else if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                {
                    return null;
                }
                else
                {
                    resp.EnsureSuccessStatusCode();
                    body = await resp.Content.ReadAsStringAsync();
                    SpeedUp();
                }
            }
            finally
            {
                _requests.Release();
            }

            if (retryAfter is { } wait)
            {
                SlowDown();
                if (wait > _maxBackoff)
                    throw new HttpRequestException(
                        $"Spotify is rate limiting this app for {Describe(wait)}, which is longer than the " +
                        $"{Describe(_maxBackoff)} this importer will wait. Raise SpotifyMinIntervalMs in config.json " +
                        "and try again later. Everything already imported is kept, so the next run resumes where this one stopped.");

                // One shared penalty window: a 429 means the whole app is over quota, so every other lookup
                // in flight has to hold off too. Letting them carry on just extends the penalty for everyone.
                StartBackoff(wait + TimeSpan.FromSeconds(1));
                continue;
            }

            if (unauthorized)
            {
                _token = null;
                continue;
            }

            return JsonDocument.Parse(body);
        }
        throw new HttpRequestException($"Spotify request kept failing: {url}");
    }

    /// <summary>Retry-After is allowed to be either a number of seconds or an absolute date.</summary>
    static TimeSpan RetryAfter(HttpResponseMessage resp)
    {
        var header = resp.Headers.RetryAfter;
        var wait = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return wait is { } w && w > TimeSpan.Zero ? w : TimeSpan.FromSeconds(5);
    }

    async Task WaitOutBackoffAsync()
    {
        while (true)
        {
            TimeSpan remaining;
            lock (_backoffLock) remaining = _backoffUntil - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return;
            await Task.Delay(remaining);
        }
    }

    void StartBackoff(TimeSpan wait)
    {
        lock (_backoffLock)
        {
            var until = DateTime.UtcNow + wait;
            if (until <= _backoffUntil) return;
            _backoffUntil = until;
        }
        // Never back off silently: without this the importer looks like it has hung.
        Console.WriteLine($"  spotify rate limited, pausing lookups for {Describe(wait)}");
    }

    static string Describe(TimeSpan t) =>
        t.TotalMinutes < 1 ? $"{t.TotalSeconds:N0}s"
        : t.TotalHours < 1 ? $"{t.TotalMinutes:N0}m"
        : $"{t.TotalHours:N1}h";

    /// <summary>
    /// Spotify's quota is a request rate, not a concurrency limit, so capping how many calls are in flight
    /// is not enough on its own. Every call books the next free slot and waits for it, and the gap between
    /// slots grows each time we are told off and shrinks again while things are going well. Importing five
    /// years of links is thousands of lookups, and tripping the limit repeatedly earns bans measured in hours.
    /// </summary>
    async Task PaceAsync()
    {
        DateTime slot;
        lock (_paceLock)
        {
            if (_minInterval == TimeSpan.Zero) return;
            var now = DateTime.UtcNow;
            slot = _nextSlot > now ? _nextSlot : now;
            _nextSlot = slot + _minInterval;
        }
        var delay = slot - DateTime.UtcNow;
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
    }

    void SlowDown()
    {
        lock (_paceLock)
        {
            _successes = 0;
            var floor = _baseInterval > TimeSpan.Zero ? _baseInterval : TimeSpan.FromMilliseconds(250);
            var slower = _minInterval < floor ? floor : _minInterval * 2;
            _minInterval = slower > MaxInterval ? MaxInterval : slower;
        }
    }

    /// <summary>
    /// Recovers from a penalty, but never below the configured starting pace. Returning all the way to
    /// unpaced is what makes a long run trip the limit over and over instead of just once.
    /// </summary>
    void SpeedUp()
    {
        lock (_paceLock)
        {
            if (_minInterval <= _baseInterval || ++_successes < 50) return;
            _successes = 0;
            var faster = _minInterval / 2;
            _minInterval = faster < _baseInterval ? _baseInterval : faster;
        }
    }

    public async Task<SpotifyTrack?> GetTrackAsync(string id)
    {
        using var doc = await GetJsonAsync($"https://api.spotify.com/v1/tracks/{id}?market={_market}");
        return doc == null ? null : ParseTrack(doc.RootElement);
    }

    /// <summary>
    /// Finds the Spotify track for a recording by its ISRC. This is an exact lookup, not a guess, so it is
    /// the best way to turn a Deezer/Apple link into Spotify artist and genre data.
    /// </summary>
    public async Task<SpotifyTrack?> FindByIsrcAsync(string isrc)
    {
        if (string.IsNullOrWhiteSpace(isrc)) return null;
        var results = await SearchTracksAsync($"isrc:{isrc.Trim().ToUpperInvariant()}");
        return results.FirstOrDefault();
    }

    public async Task<List<SpotifyTrack>> GetPlaylistTracksAsync(string id)
    {
        var tracks = new List<SpotifyTrack>();
        // A playlist is not only catalogue tracks: it can also hold podcast episodes and the owner's own
        // local files. All three are entries, so `type`/`is_local` are requested to tell them apart, and
        // the episode-only fields (`show`, episode `release_date`) are asked for alongside the track ones.
        string? url = $"https://api.spotify.com/v1/playlists/{id}/tracks?limit=100&market={_market}" +
                      "&additional_types=track,episode" +
                      "&fields=next,items(track(id,name,type,is_local,duration_ms,release_date," +
                      "external_ids(isrc),artists(id,name),album(release_date),show(name,publisher)))";
        while (url != null)
        {
            using var doc = await GetJsonAsync(url);
            if (doc == null) break;
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                if (item.TryGetProperty("track", out var t) && t.ValueKind == JsonValueKind.Object)
                {
                    var track = ParsePlaylistItem(t);
                    if (track != null) tracks.Add(track);
                }
            }
            url = doc.RootElement.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString() : null;
        }
        return tracks;
    }

    /// <summary>A podcast episode by id, for a link that points straight at one.</summary>
    public async Task<SpotifyTrack?> GetEpisodeAsync(string id)
    {
        using var doc = await GetJsonAsync($"https://api.spotify.com/v1/episodes/{id}?market={_market}");
        return doc == null ? null : ParseEpisode(doc.RootElement);
    }

    public async Task<List<SpotifyTrack>> SearchTracksAsync(string query)
    {
        return await CachedAsync(_searchCache, query, async () =>
        {
            using var doc = await GetJsonAsync(
                $"https://api.spotify.com/v1/search?type=track&limit=20&market={_market}&q={Uri.EscapeDataString(query)}");
            var result = new List<SpotifyTrack>();
            if (doc == null || !doc.RootElement.TryGetProperty("tracks", out var tracks)) return result;
            foreach (var t in tracks.GetProperty("items").EnumerateArray())
            {
                var track = ParseTrack(t);
                if (track != null) result.Add(track);
            }
            return result;
        });
    }

    public async Task<List<string>> GetArtistGenresAsync(string artistId)
    {
        return await CachedAsync(_genreCache, artistId, async () =>
        {
            using var doc = await GetJsonAsync($"https://api.spotify.com/v1/artists/{artistId}");
            var genres = new List<string>();
            if (doc != null && doc.RootElement.TryGetProperty("genres", out var g))
                foreach (var x in g.EnumerateArray())
                    if (x.GetString() is { Length: > 0 } name) genres.Add(name);
            return genres;
        });
    }

    /// <summary>
    /// Answers a repeated question from the first answer. Several messages are prepared at once, so the
    /// work is held as a lazy task and shared: callers that ask while a lookup is still in flight wait for
    /// it rather than starting a second identical request. A failed lookup is evicted so that one transient
    /// network error does not poison that query for the rest of the run.
    /// </summary>
    static async Task<T> CachedAsync<T>(ConcurrentDictionary<string, Lazy<Task<T>>> cache, string key,
        Func<Task<T>> fetch)
    {
        var entry = cache.GetOrAdd(key,
            _ => new Lazy<Task<T>>(fetch, LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await entry.Value;
        }
        catch
        {
            cache.TryRemove(new KeyValuePair<string, Lazy<Task<T>>>(key, entry));
            throw;
        }
    }

    /// <summary>
    /// One row of a playlist. That is not always a catalogue track: it can also be a podcast episode or a
    /// file from the owner's own machine. All three are songs of the day, so none of them is dropped —
    /// they only differ in how much Spotify knows about them.
    /// </summary>
    public static SpotifyTrack? ParsePlaylistItem(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        if (t.TryGetProperty("is_local", out var local) && local.ValueKind == JsonValueKind.True)
            return ParseLocalFile(t);
        var type = t.TryGetProperty("type", out var ty) ? ty.GetString() : null;
        return type == "episode" ? ParseEpisode(t) : ParseTrack(t);
    }

    static SpotifyTrack? ParseTrack(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        if (!t.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) return null;
        if (t.TryGetProperty("type", out var type) && type.GetString() != "track") return null;
        // A local file has no catalogue data at all, so it cannot be read as a track.
        if (t.TryGetProperty("is_local", out var local) && local.ValueKind == JsonValueKind.True) return null;

        DateTime? release = null;
        if (t.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object &&
            album.TryGetProperty("release_date", out var rd) && rd.ValueKind == JsonValueKind.String)
            release = ParseReleaseDate(rd.GetString());

        string? isrc = null;
        if (t.TryGetProperty("external_ids", out var ext) && ext.ValueKind == JsonValueKind.Object &&
            ext.TryGetProperty("isrc", out var isrcEl) && isrcEl.ValueKind == JsonValueKind.String)
            isrc = NormalizeIsrc(isrcEl.GetString());

        return new SpotifyTrack(
            idEl.GetString()!,
            ParseName(t) ?? "",
            ParseArtists(t),
            ParseSeconds(t),
            release,
            isrc);
    }

    /// <summary>
    /// A podcast episode. It has a real Spotify id, but never an ISRC and never an artist, so the show it
    /// belongs to is credited instead — that is who a listener would name.
    /// </summary>
    public static SpotifyTrack? ParseEpisode(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        if (ParseName(t) is not { } name) return null;
        var id = t.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString() : null;

        var artists = new List<MediaArtist>();
        if (t.TryGetProperty("show", out var show) && show.ValueKind == JsonValueKind.Object)
        {
            var showName = show.TryGetProperty("name", out var sn) ? sn.GetString() : null;
            if (string.IsNullOrWhiteSpace(showName))
                showName = show.TryGetProperty("publisher", out var pub) ? pub.GetString() : null;
            if (!string.IsNullOrWhiteSpace(showName)) artists.Add(new MediaArtist(showName.Trim(), null));
        }

        // An episode dates itself; there is no album to ask.
        DateTime? release = t.TryGetProperty("release_date", out var rd) && rd.ValueKind == JsonValueKind.String
            ? ParseReleaseDate(rd.GetString())
            : null;

        return new SpotifyTrack(id, name, artists, ParseSeconds(t), release, null, SpotifyItemKind.Episode);
    }

    /// <summary>
    /// A file from the playlist owner's own machine. Spotify holds no catalogue data for it — no id, no
    /// ISRC, no public URL, and an album with no usable date — but the title and artist tags it does carry
    /// are enough to record what was posted, so the row is imported as-is rather than dropped.
    /// </summary>
    static SpotifyTrack? ParseLocalFile(JsonElement t)
    {
        if (ParseName(t) is not { } name) return null;
        return new SpotifyTrack(null, name, ParseArtists(t), ParseSeconds(t), null, null, SpotifyItemKind.LocalFile);
    }

    static string? ParseName(JsonElement t) =>
        t.TryGetProperty("name", out var n) && n.GetString() is { } name && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : null;

    static List<MediaArtist> ParseArtists(JsonElement t)
    {
        var artists = new List<MediaArtist>();
        if (!t.TryGetProperty("artists", out var arr) || arr.ValueKind != JsonValueKind.Array) return artists;
        foreach (var a in arr.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var aid = a.TryGetProperty("id", out var ai) && ai.ValueKind == JsonValueKind.String ? ai.GetString() : null;
            artists.Add(new MediaArtist(name, aid));
        }
        return artists;
    }

    static int ParseSeconds(JsonElement t)
    {
        var ms = t.TryGetProperty("duration_ms", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0;
        return (int)Math.Round(ms / 1000.0);
    }

    /// <summary>ISRCs are 12 alphanumeric characters; services format them with or without dashes.</summary>
    public static string? NormalizeIsrc(string? isrc)
    {
        if (string.IsNullOrWhiteSpace(isrc)) return null;
        var cleaned = new string(isrc.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return cleaned.Length == 12 ? cleaned : null;
    }

    /// <summary>Spotify dates are "yyyy", "yyyy-MM" or "yyyy-MM-dd".</summary>
    public static DateTime? ParseReleaseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var formats = new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy" };
        return DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
    }
}
