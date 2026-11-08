using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Importer;

public static class Text
{
    static readonly Regex UrlRx = new(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Bracket characters seen in video titles, including the CJK ones.
    const string OpenBrackets = @"\(\[\{【『「（";
    const string CloseBrackets = @"\)\]\}】』」）";

    /// <summary>Words that mean "this part of the title is packaging, not the song".</summary>
    const string NoiseWords =
        @"official|lyrics?|lyric\s+video|visuali[sz]er|audio|video|m[\/\.]?v|hd|hq|4k|8k|uhd|" +
        @"remaster(?:ed|ing)?|explicit|clean\s+version|free\s+download|out\s+now|color(?:\s+|-)?coded|" +
        @"eng(?:lish)?\s+sub(?:title)?s?|sub(?:title)?s|prod\.?\s+by|full\s+song|high\s+quality|" +
        @"with\s+lyrics|letra|legendado|performance\s+video|dolby\s+atmos";

    static readonly Regex BracketNoiseRx = new(
        $@"\s*[{OpenBrackets}][^{CloseBrackets}]*\b(?:{NoiseWords})\b[^{CloseBrackets}]*[{CloseBrackets}]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex EmptyBracketRx = new(
        $@"\s*[{OpenBrackets}]\s*[{CloseBrackets}]", RegexOptions.Compiled);

    // "... | Official Music Video", possibly repeated.
    static readonly Regex TrailingPipeNoiseRx = new(
        $@"\s*[|｜]\s*[^|｜]*\b(?:{NoiseWords}|from\s+the\s+album|new\s+single)\b[^|｜]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "... - Official Video" (a dash is normally the artist/title separator, so only strip a pure noise tail).
    static readonly Regex TrailingDashNoiseRx = new(
        @"\s*[-–—]\s*(?:(?:official|lyrics?|music|performance|the)\s+)*(?:video|audio|visuali[sz]er|m[\/\.]?v|hd|hq|4k)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "feat. X", "(ft. X & Y)", "w/ X"
    static readonly Regex FeatBracketRx = new(
        $@"[{OpenBrackets}]\s*(?:feat|ft|featuring)\b\.?\s*(?<names>[^{CloseBrackets}]+)[{CloseBrackets}]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex FeatTailRx = new(
        @"\s+(?:feat|ft|featuring)\b\.?\s+(?<names>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex FeatSlashRx = new(
        @"\s+w/\s+(?<names>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Splits "A, B & C" into separate credited names.
    static readonly Regex ArtistSplitRx = new(
        @"\s*(?:,|&|×|\sx\s|\s\+\s|;)\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Release-variant markers. The same recording is reissued with these glued on, so they are dropped when
    /// deciding whether two rows are the same song. Deliberately excludes live/remix/acoustic/cover/instrumental
    /// and "Taylor's Version", which really are different recordings.
    /// </summary>
    static readonly Regex VersionSuffixRx = new(
        $@"\s*(?:[-–—]|[{OpenBrackets}])\s*(?:(?:19|20)\d{{2}}\s+)?(?:digitally\s+|digital\s+)?" +
        @"(?:re-?master(?:ed|ing)?|single\s+version|album\s+version|radio\s+edit|radio\s+version|" +
        @"original\s+mix|original\s+version|mono(?:\s+version)?|stereo(?:\s+version)?|deluxe(?:\s+edition)?|" +
        @"bonus\s+track|explicit(?:\s+version)?|clean(?:\s+version)?|anniversary\s+edition|expanded\s+edition)" +
        $@"(?:\s+(?:19|20)\d{{2}})?(?:\s+version)?\s*(?:[{CloseBrackets}])?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex WhitespaceRx = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Uploader names that are a channel, a label or a placeholder rather than an artist.</summary>
    static readonly Regex ChannelNameRx = new(
        @"vevo\b|\btopic\b|\blyrics?\b|\bmusic\s+video\b|various\s+artists|unknown\s+artist|" +
        @"auto-?generated\s+by\s+youtube|(?:records|recordings|entertainment|productions?|studios|" +
        @"official|uploads|archive|channel|network|music\s+group)\s*$",
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
        return WhitespaceRx.Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>
    /// The key used to decide whether two songs are the same recording: the normalized title with
    /// featured credits and reissue markers ("- Remastered 2011", "- Radio Edit") removed.
    /// </summary>
    public static string NormalizeForMatching(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var s = SplitFeatured(title).Title;
        string previous;
        do
        {
            previous = s;
            s = VersionSuffixRx.Replace(s, "").Trim();
        } while (s != previous && s.Length > 0);

        var normalized = NormalizeTitle(s);
        return normalized.Length > 0 ? normalized : NormalizeTitle(title);
    }

    /// <summary>Strips noise like "(Official Video)", "| Official Music Video" and "- Official Audio".</summary>
    public static string CleanVideoTitle(string title)
    {
        var s = title ?? "";
        string previous;
        do
        {
            previous = s;
            s = BracketNoiseRx.Replace(s, "");
            s = TrailingPipeNoiseRx.Replace(s, "");
            s = TrailingDashNoiseRx.Replace(s, "");
            s = EmptyBracketRx.Replace(s, "");
            s = s.Trim();
        } while (s != previous && s.Length > 0);

        s = WhitespaceRx.Replace(s, " ").Trim(' ', '-', '–', '—', '|', '｜', '_');
        return StripSurroundingQuotes(s);
    }

    /// <summary>Removes matching quotes or CJK brackets wrapped around a whole title.</summary>
    public static string StripSurroundingQuotes(string s)
    {
        var pairs = new[] { ('"', '"'), ('\'', '\''), ('“', '”'), ('‘', '’'), ('「', '」'), ('『', '』'), ('《', '》') };
        var t = s.Trim();
        foreach (var (open, close) in pairs)
            if (t.Length > 2 && t[0] == open && t[^1] == close)
                return t[1..^1].Trim();
        return t;
    }

    /// <summary>
    /// Pulls "feat. X" / "(ft. X &amp; Y)" / "w/ X" out of a title. Returns the title without it and the
    /// featured names, which are worth knowing even when the title itself is kept.
    /// </summary>
    public static (string Title, List<string> Featured) SplitFeatured(string title)
    {
        var featured = new List<string>();
        var s = title ?? "";
        foreach (var rx in new[] { FeatBracketRx, FeatSlashRx, FeatTailRx })
        {
            for (var m = rx.Match(s); m.Success; m = rx.Match(s))
            {
                featured.AddRange(SplitArtistNames(m.Groups["names"].Value));
                s = s.Remove(m.Index, m.Length);
            }
        }
        s = EmptyBracketRx.Replace(s, "");
        s = WhitespaceRx.Replace(s, " ").Trim(' ', '-', '–', '—');
        return (s.Length > 0 ? s : (title ?? "").Trim(), Dedupe(featured));
    }

    /// <summary>Splits a credit string like "A, B &amp; C" into separate names.</summary>
    public static List<string> SplitArtistNames(string names)
    {
        var parts = ArtistSplitRx.Split(names ?? "")
            .Select(p => StripSurroundingQuotes(p.Trim(' ', '-', '–', '—', '.')))
            .Where(p => p.Length > 0);
        return Dedupe(parts);
    }

    /// <summary>Strips "- Topic", "VEVO" and "Official Channel" style decoration from a channel name.</summary>
    public static string CleanUploader(string uploader)
    {
        var u = (uploader ?? "").Trim();
        string[] suffixes = { " - Topic", "- Topic", " Official Channel", " - Official", " Official", "VEVO", " Music Channel", " Channel" };
        bool changed;
        do
        {
            changed = false;
            foreach (var suffix in suffixes)
            {
                if (u.Length <= suffix.Length || !u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                u = u[..^suffix.Length].Trim(' ', '-', '–', '—', '|');
                changed = true;
            }
        } while (changed && u.Length > 0);
        return u;
    }

    /// <summary>
    /// True when a name is a channel, a label or a placeholder rather than a real artist. Used to stop
    /// rows like "RadioheadVEVO" or "Various Artists" being created when nothing better could be found.
    /// </summary>
    public static bool IsLikelyChannelName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return ChannelNameRx.IsMatch(name.Trim());
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

    static readonly string[] DashSeparators = { " - ", " – ", " — ", " ‒ ", " ― ", " − ", " -- ", "－" };

    /// <summary>
    /// Every plausible way to read a raw title as "artist" plus "song", best guess first. A title like
    /// "A - B - C" is ambiguous, so both the first and the last split are offered and the Spotify search
    /// decides which one actually exists.
    /// </summary>
    public static List<(string Artist, string Title)> ArtistTitleCandidates(string raw)
    {
        var result = new List<(string Artist, string Title)>();
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return result;

        void Add(string artist, string title)
        {
            artist = StripSurroundingQuotes(artist.Trim(' ', '-', '–', '—', ':'));
            title = StripSurroundingQuotes(title.Trim(' ', '-', '–', '—', ':'));
            if (artist.Length == 0 || title.Length == 0) return;
            if (!result.Any(x => x.Artist == artist && x.Title == title)) result.Add((artist, title));
        }

        foreach (var sep in DashSeparators)
        {
            var first = s.IndexOf(sep, StringComparison.Ordinal);
            if (first <= 0) continue;
            Add(s[..first], s[(first + sep.Length)..]);
            var last = s.LastIndexOf(sep, StringComparison.Ordinal);
            if (last != first) Add(s[..last], s[(last + sep.Length)..]);
        }

        // Artist "Title" / Artist 「Title」
        foreach (var (open, close) in new[] { ('"', '"'), ('“', '”'), ('「', '」'), ('『', '』') })
        {
            var start = s.IndexOf(open);
            var end = s.LastIndexOf(close);
            if (start > 0 && end > start + 1) Add(s[..start], s[(start + 1)..end]);
        }

        // "Artist: Title"
        var colon = s.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0) Add(s[..colon], s[(colon + 2)..]);

        // "Title by Artist"
        var by = s.LastIndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (by > 0) Add(s[(by + 4)..], s[..by]);

        return result;
    }

    /// <summary>True when every token of <paramref name="needleNorm"/> appears as a run of tokens in the haystack.</summary>
    public static bool ContainsAsTokens(string haystackNorm, string needleNorm)
    {
        if (needleNorm.Length == 0) return false;
        var hay = haystackNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var needle = needleNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (needle.Length == 0 || needle.Length > hay.Length) return false;
        for (var i = 0; i + needle.Length <= hay.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length && ok; j++)
                if (hay[i + j] != needle[j]) ok = false;
            if (ok) return true;
        }
        return false;
    }

    /// <summary>Dice coefficient over distinct tokens: 1 is identical, 0 shares nothing.</summary>
    public static double TokenSimilarity(string aNorm, string bNorm)
    {
        var a = aNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var b = bNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (a.Count == 0 || b.Count == 0) return 0;
        var shared = a.Count(b.Contains);
        return 2.0 * shared / (a.Count + b.Count);
    }

    static List<string> Dedupe(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var v in values)
            if (v.Length > 0 && seen.Add(v)) result.Add(v);
        return result;
    }
}
