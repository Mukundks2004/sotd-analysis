using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Sotd.Importer;

public record PageMetadata(string Title, string? Artist, int? DurationSeconds, DateTime? Date);

/// <summary>
/// Fallback for hosts yt-dlp cannot read (Apple Music, Tidal, Amazon Music, ...):
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

    readonly HttpClient _http;

    public PageMetadataClient(HttpClient http) => _http = http;

    public async Task<PageMetadata?> GetAsync(string url)
    {
        var html = await _http.GetStringAsync(url);
        return Parse(html);
    }

    public static PageMetadata? Parse(string html)
    {
        foreach (Match m in JsonLdRx.Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value));
                var found = FindRecording(doc.RootElement);
                if (found != null) return found;
            }
            catch (JsonException) { /* not usable, try the next block */ }
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

    static PageMetadata? FindRecording(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray())
                    if (FindRecording(x) is { } r) return r;
                return null;
            case JsonValueKind.Object:
                if (e.TryGetProperty("@graph", out var graph) && FindRecording(graph) is { } g) return g;
                if (e.TryGetProperty("@type", out var type) && type.ToString().Contains("Music") &&
                    e.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    int? duration = null;
                    if (e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.String)
                    {
                        try { duration = (int)XmlConvert.ToTimeSpan(d.GetString()!).TotalSeconds; }
                        catch (FormatException) { }
                    }
                    DateTime? date = null;
                    if (e.TryGetProperty("datePublished", out var dp) && dp.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(dp.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal |
                            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                        date = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

                    string? artist = null;
                    if (e.TryGetProperty("byArtist", out var by)) artist = ArtistNames(by);
                    return new PageMetadata(DecodeText(name.GetString()!), artist, duration, date);
                }
                return null;
            default:
                return null;
        }
    }

    static string DecodeText(string s) => WebUtility.HtmlDecode(s).Trim();

    static string? ArtistNames(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Object => e.TryGetProperty("name", out var n) ? n.GetString() : null,
        JsonValueKind.Array => string.Join(", ", e.EnumerateArray().Select(ArtistNames).Where(x => !string.IsNullOrWhiteSpace(x))),
        _ => null,
    };
}
