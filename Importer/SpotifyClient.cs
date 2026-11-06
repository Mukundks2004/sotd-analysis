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
        // `type` and `is_local` must be requested explicitly: without them every podcast episode in a
        // playlist parses as a track and gets imported as a song.
        string? url = $"https://api.spotify.com/v1/playlists/{id}/tracks?limit=100&market={_market}" +
                      "&additional_types=track" +
                      "&fields=next,items(track(id,name,type,is_local,duration_ms,external_ids(isrc)," +
                      "artists(id,name),album(release_date)))";
        while (url != null)
        {
            using var doc = await GetJsonAsync(url);
            if (doc == null) break;
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                if (item.TryGetProperty("track", out var t) && t.ValueKind == JsonValueKind.Object)
                {
                    var track = ParseTrack(t);
                    if (track != null) tracks.Add(track);
                }
            }
            url = doc.RootElement.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString() : null;
        }
        return tracks;
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

    static SpotifyTrack? ParseTrack(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        if (!t.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) return null;
        if (t.TryGetProperty("type", out var type) && type.GetString() != "track") return null;
        // A "local file" row in a playlist is not a catalogue track and has no usable ids.
        if (t.TryGetProperty("is_local", out var local) && local.ValueKind == JsonValueKind.True) return null;

        var artists = new List<MediaArtist>();
        if (t.TryGetProperty("artists", out var arr))
            foreach (var a in arr.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var aid = a.TryGetProperty("id", out var ai) && ai.ValueKind == JsonValueKind.String ? ai.GetString() : null;
                artists.Add(new MediaArtist(name, aid));
            }

        DateTime? release = null;
        if (t.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object &&
            album.TryGetProperty("release_date", out var rd) && rd.ValueKind == JsonValueKind.String)
            release = ParseReleaseDate(rd.GetString());

        string? isrc = null;
        if (t.TryGetProperty("external_ids", out var ext) && ext.ValueKind == JsonValueKind.Object &&
            ext.TryGetProperty("isrc", out var isrcEl) && isrcEl.ValueKind == JsonValueKind.String)
            isrc = NormalizeIsrc(isrcEl.GetString());

        var durationMs = t.TryGetProperty("duration_ms", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0;
        return new SpotifyTrack(
            idEl.GetString()!,
            t.GetProperty("name").GetString() ?? "",
            artists,
            (int)Math.Round(durationMs / 1000.0),
            release,
            isrc);
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
