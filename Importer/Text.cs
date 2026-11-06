using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sotd.Importer;

public static class Text
{
    static readonly Regex UrlRx = new(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex NoiseRx = new(
        @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|visuali[sz]er|hd|hq|4k|mv|remaster(ed)?)\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>All distinct http(s) links in a message, in order of appearance.</summary>
    public static List<string> ExtractLinks(string? content)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(content)) return result;
        foreach (Match m in UrlRx.Matches(content))
        {
            var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '\'', '"', '*', '|', '~');
            if (url.Length > 8 && !result.Contains(url)) result.Add(url);
        }
        return result;
    }

    /// <summary>Joins path parts with '/' so paths stored in the db look the same on every OS.</summary>
    public static string RelativePath(params string[] parts) => string.Join('/', parts);

    /// <summary>Makes a string safe as a file name, keeping the extension and capping the stem length.</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
        var s = sb.ToString().Trim();
        var ext = Path.GetExtension(s);
        if (ext.Length > 12) ext = "";
        var stem = ext.Length > 0 ? s[..^ext.Length] : s;
        stem = stem.TrimEnd('.', ' ');
        if (stem.Length > 100) stem = stem[..100];
        if (stem.Length == 0) stem = "_";
        return stem + ext;
    }

    /// <summary>Lowercase, accent-free, punctuation-free, single-spaced. Used to compare titles.</summary>
    public static string NormalizeTitle(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>Strips noise like "(Official Video)" from a video title.</summary>
    public static string CleanVideoTitle(string title) => NoiseRx.Replace(title, "").Trim();

    /// <summary>Strips the " - Topic" suffix YouTube adds to auto-generated artist channels.</summary>
    public static string CleanUploader(string uploader)
    {
        var u = uploader.Trim();
        return u.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase) ? u[..^8].Trim() : u;
    }

    /// <summary>Splits "Artist - Title" into its parts, if it has that shape.</summary>
    public static (string Artist, string Title)? TryParseArtistTitle(string raw)
    {
        foreach (var sep in new[] { " - ", " – ", " — " })
        {
            var i = raw.IndexOf(sep, StringComparison.Ordinal);
            if (i <= 0) continue;
            var artist = raw[..i].Trim();
            var title = raw[(i + sep.Length)..].Trim();
            if (artist.Length > 0 && title.Length > 0) return (artist, title);
        }
        return null;
    }
}
