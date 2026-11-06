using System.Globalization;
using System.Text.Json;

namespace Sotd.Importer;

/// <summary>
/// Apple Music metadata via the public iTunes lookup API. Apple Music pages cannot be read by yt-dlp and
/// their HTML describes the *album*, so scraping used to put the album name in the song title and leave the
/// duration at 0. The lookup API needs no key and answers with the exact track.
/// </summary>
public class ITunesClient
{
    readonly HttpClient _http;
    readonly string _country;

    public ITunesClient(HttpClient http, string country = "US")
    {
        _http = http;
        _country = string.IsNullOrWhiteSpace(country) ? "US" : country.Trim().ToUpperInvariant();
    }

    public async Task<ProviderTrack?> GetTrackAsync(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return null;
        var url = $"https://itunes.apple.com/lookup?id={Uri.EscapeDataString(trackId)}&entity=song&country={_country}";
        string body;
        try
        {
            body = await _http.GetStringAsync(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
        return Parse(body);
    }

    public static ProviderTrack? Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array) return null;

            foreach (var r in results.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                var kind = Str(r, "kind");
                if (kind != null && kind != "song" && kind != "music-video") continue;
                var title = Str(r, "trackName") ?? Str(r, "trackCensoredName");
                if (string.IsNullOrWhiteSpace(title)) continue;

                var artist = Str(r, "artistName");
                int? duration = r.TryGetProperty("trackTimeMillis", out var ms) && ms.ValueKind == JsonValueKind.Number
                    ? (int)Math.Round(ms.GetInt64() / 1000.0)
                    : null;

                DateTime? released = null;
                if (Str(r, "releaseDate") is { } rd && DateTime.TryParse(rd, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                    released = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

                var artists = artist != null ? Text.SplitArtistNames(artist) : new List<string>();
                return new ProviderTrack(title!.Trim(), artists, duration, released);
            }
            return null;
        }
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Deezer metadata via its public API (no key needed). Deezer returns the ISRC, which is what ties the same
/// recording to its Spotify and Apple Music counterparts.
/// </summary>
public class DeezerClient
{
    readonly HttpClient _http;

    public DeezerClient(HttpClient http) => _http = http;

    public async Task<ProviderTrack?> GetTrackAsync(string trackId)
    {
        var body = await TryGetAsync($"https://api.deezer.com/track/{Uri.EscapeDataString(trackId)}");
        return body == null ? null : ParseTrack(body);
    }

    public async Task<List<ProviderTrack>> GetPlaylistTracksAsync(string playlistId)
    {
        var body = await TryGetAsync($"https://api.deezer.com/playlist/{Uri.EscapeDataString(playlistId)}");
        return body == null ? new() : ParsePlaylist(body);
    }

    async Task<string?> TryGetAsync(string url)
    {
        try { return await _http.GetStringAsync(url); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    public static ProviderTrack? ParseTrack(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
        using (doc) return ParseTrack(doc.RootElement);
    }

    public static List<ProviderTrack> ParsePlaylist(string json)
    {
        var result = new List<ProviderTrack>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return result; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("tracks", out var tracks) ||
                !tracks.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var t in data.EnumerateArray())
                if (ParseTrack(t) is { } track) result.Add(track);
            return result;
        }
    }

    static ProviderTrack? ParseTrack(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        if (t.TryGetProperty("error", out _)) return null;

        var title = Str(t, "title_short") ?? Str(t, "title");
        if (string.IsNullOrWhiteSpace(title)) return null;

        var artists = new List<string>();
        if (t.TryGetProperty("contributors", out var contributors) && contributors.ValueKind == JsonValueKind.Array)
            foreach (var c in contributors.EnumerateArray())
                if (Str(c, "name") is { Length: > 0 } name && !artists.Contains(name)) artists.Add(name);
        if (artists.Count == 0 && t.TryGetProperty("artist", out var artist) && Str(artist, "name") is { Length: > 0 } one)
            artists.Add(one);

        int? duration = t.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
            ? d.GetInt32() : null;

        DateTime? released = null;
        if (Str(t, "release_date") is { } rd && DateTime.TryParse(rd, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            released = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

        return new ProviderTrack(title!.Trim(), artists, duration, released, SpotifyClient.NormalizeIsrc(Str(t, "isrc")));
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
