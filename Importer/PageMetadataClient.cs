using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Sotd.Importer;

public record PageMetadata(string Title, string? Artist, int? DurationSeconds, DateTime? Date);

/// <summary>
/// Fallback for hosts with no usable API and that yt-dlp cannot read (Tidal, Amazon Music, ...):
/// reads schema.org JSON-LD and Open Graph tags from the page.
/// </summary>
public class PageMetadataClient
{
    static readonly Regex JsonLdRx = new(
        @"<script[^>]*type=[""']application/ld\+json[""'][^>]*>(.*?)</script>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex OgTitleRx = new(
        @"<meta[^>]*property=[""']og:title[""'][^>]*content=[""']([^""']*)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ByArtistRx = new(
        @"^(.*?)\s+by\s+(.+?)(?:\s+on\s+Apple Music|\s+\|.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>schema.org types that describe one recording, best first.</summary>
    static readonly string[] RecordingTypes = { "MusicRecording", "MusicVideoObject", "AudioObject", "VideoObject" };

    readonly HttpClient _http;

    public PageMetadataClient(HttpClient http) => _http = http;

    public async Task<PageMetadata?> GetAsync(string url, string? preferTrackId = null)
    {
        var html = await _http.GetStringAsync(url);
        return Parse(html, preferTrackId);
    }

    /// <param name="preferTrackId">
    /// When the page describes an album, the id of the one track we actually want (an Apple Music
    /// "?i=" value, for example). Without it an album page is ignored rather than mistaken for a song.
    /// </param>
    public static PageMetadata? Parse(string html, string? preferTrackId = null)
    {
        var nodes = new List<JsonElement>();
        var docs = new List<JsonDocument>();
        try
        {
            foreach (Match m in JsonLdRx.Matches(html))
            {
                try
                {
                    var doc = JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value));
                    docs.Add(doc);
                    Flatten(doc.RootElement, nodes);
                }
                catch (JsonException) { /* not usable, try the next block */ }
            }

            // When the link names a specific track on an album page, that track is the answer: the page's
            // other recordings are just the rest of the tracklist.
            if (preferTrackId != null)
                foreach (var node in nodes)
                    if (FindTrackInAlbum(node, preferTrackId) is { } track)
                        return track;

            // Otherwise a node that is explicitly one recording wins over anything else on the page.
            foreach (var type in RecordingTypes)
                foreach (var node in nodes)
                    if (HasType(node, type) && ToMetadata(node) is { } found)
                        return found;
        }
        finally
        {
            foreach (var doc in docs) doc.Dispose();
        }

        var og = OgTitleRx.Match(html);
        if (!og.Success) return null;
        var title = WebUtility.HtmlDecode(og.Groups[1].Value).Trim();
        if (title.Length == 0) return null;
        var by = ByArtistRx.Match(title);
        return by.Success
            ? new PageMetadata(by.Groups[1].Value.Trim(), by.Groups[2].Value.Trim(), null, null)
            : new PageMetadata(title, null, null, null);
    }

    /// <summary>Walks the JSON-LD structure (including "@graph" and arrays) into a flat list of objects.</summary>
    static void Flatten(JsonElement e, List<JsonElement> into)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) Flatten(x, into);
                break;
            case JsonValueKind.Object:
                into.Add(e);
                if (e.TryGetProperty("@graph", out var graph)) Flatten(graph, into);
                foreach (var name in new[] { "track", "tracks", "itemListElement", "item" })
                    if (e.TryGetProperty(name, out var child)) Flatten(child, into);
                break;
        }
    }

    /// <summary>"@type" may be a string or an array of strings.</summary>
    static bool HasType(JsonElement e, string type)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("@type", out var t)) return false;
        return t.ValueKind switch
        {
            JsonValueKind.String => string.Equals(t.GetString(), type, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => t.EnumerateArray().Any(x =>
                x.ValueKind == JsonValueKind.String && string.Equals(x.GetString(), type, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    /// <summary>Picks the track whose url carries <paramref name="trackId"/> out of an album's track list.</summary>
    static PageMetadata? FindTrackInAlbum(JsonElement node, string trackId)
    {
        if (!HasType(node, "MusicAlbum") && !HasType(node, "MusicPlaylist")) return null;
        var tracks = new List<JsonElement>();
        foreach (var name in new[] { "track", "tracks", "itemListElement" })
            if (node.TryGetProperty(name, out var child)) Flatten(child, tracks);

        foreach (var track in tracks)
        {
            if (!track.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String) continue;
            var value = url.GetString() ?? "";
            if (!value.Contains(trackId, StringComparison.Ordinal)) continue;
            if (ToMetadata(track) is not { } found) continue;
            // Tracklist entries usually carry only a name, so fall back to the album's credit.
            return found.Artist is { Length: > 0 } ? found : found with { Artist = ToMetadata(node)?.Artist };
        }
        return null;
    }

    static PageMetadata? ToMetadata(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (!e.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) return null;
        var title = DecodeText(name.GetString()!);
        if (title.Length == 0) return null;

        int? duration = null;
        if (e.TryGetProperty("duration", out var d))
        {
            if (d.ValueKind == JsonValueKind.String)
            {
                try { duration = (int)XmlConvert.ToTimeSpan(d.GetString()!).TotalSeconds; }
                catch (FormatException) { }
            }
            else if (d.ValueKind == JsonValueKind.Number)
            {
                duration = d.GetInt32();
            }
        }

        DateTime? date = null;
        foreach (var field in new[] { "datePublished", "uploadDate", "releaseDate", "dateCreated" })
        {
            if (!e.TryGetProperty(field, out var dp) || dp.ValueKind != JsonValueKind.String) continue;
            if (!DateTime.TryParse(dp.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)) continue;
            date = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            break;
        }

        string? artist = null;
        foreach (var field in new[] { "byArtist", "creator", "author", "publisher" })
            if (e.TryGetProperty(field, out var by) && ArtistNames(by) is { Length: > 0 } names)
            {
                artist = names;
                break;
            }

        return new PageMetadata(title, artist, duration > 0 ? duration : null, date);
    }

    static string DecodeText(string s) => WebUtility.HtmlDecode(s).Trim();

    static string? ArtistNames(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => DecodeText(e.GetString() ?? ""),
        JsonValueKind.Object => e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? DecodeText(n.GetString() ?? "") : null,
        JsonValueKind.Array => string.Join(", ", e.EnumerateArray().Select(ArtistNames).Where(x => !string.IsNullOrWhiteSpace(x))),
        _ => null,
    };
}
