using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Sotd.Importer;

/// <summary>Minimal Spotify Web API client (client-credentials flow; public catalogue data only).</summary>
public class SpotifyClient
{
    readonly HttpClient _http;
    readonly string _clientId;
    readonly string _clientSecret;
    readonly string _market;
    string? _token;
    DateTime _tokenExpires = DateTime.MinValue;

    /// <param name="market">
    /// Pinning a market matters: Spotify "track relinking" hands back a different track id for the same
    /// recording depending on the market, so an unpinned importer can store the same song twice.
    /// </param>
    public SpotifyClient(HttpClient http, string clientId, string clientSecret, string market = "US")
    {
        _http = http;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _market = string.IsNullOrWhiteSpace(market) ? "US" : market.Trim().ToUpperInvariant();
    }

    async Task<string> TokenAsync()
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

    /// <summary>GET with retry on 429/401. Returns null for 400/404 (not found / bad id).</summary>
    async Task<JsonDocument?> GetJsonAsync(string url)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
            using var resp = await _http.SendAsync(req);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                await Task.Delay(wait + TimeSpan.FromSeconds(1));
                continue;
            }
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                _token = null;
                continue;
            }
            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest) return null;
            resp.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        }
        throw new HttpRequestException($"Spotify request kept failing: {url}");
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
    }

    public async Task<List<string>> GetArtistGenresAsync(string artistId)
    {
        using var doc = await GetJsonAsync($"https://api.spotify.com/v1/artists/{artistId}");
        var genres = new List<string>();
        if (doc != null && doc.RootElement.TryGetProperty("genres", out var g))
            foreach (var x in g.EnumerateArray())
                if (x.GetString() is { Length: > 0 } name) genres.Add(name);
        return genres;
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
