using System.Text.RegularExpressions;
using System.Web;

namespace Sotd.Importer;

public enum LinkKind
{
    /// <summary>A single song/video.</summary>
    Track,
    /// <summary>A playlist; each song in it is one entry.</summary>
    Playlist,
    /// <summary>A known music/video host; let yt-dlp / page metadata work out what it is.</summary>
    Generic,
    /// <summary>Short link that must be redirected before it can be classified.</summary>
    ShortLink,
    /// <summary>Known host, but a kind of page we cannot turn into entries (album, channel, artist...).</summary>
    Unsupported,
}

public record LinkInfo(string Url, string Source, LinkKind Kind, string? SpotifyId = null);

public static class LinkClassifier
{
    static readonly Regex SpotifyPath = new(
        @"^/(?:intl-[a-z\-]+/)?(track|playlist|album|artist|episode|show)/([A-Za-z0-9]+)", RegexOptions.Compiled);

    // (domain suffix, source name). Matches the host itself or any subdomain.
    static readonly (string Domain, string Source)[] GenericHosts =
    {
        ("music.apple.com", "apple music"),
        ("itunes.apple.com", "apple music"),
        ("tidal.com", "tidal"),
        ("soundcloud.com", "soundcloud"),
        ("snd.sc", "soundcloud"),
        ("bandcamp.com", "bandcamp"),
        ("vimeo.com", "vimeo"),
        ("deezer.com", "deezer"),
        ("shazam.com", "shazam"),
        ("tiktok.com", "tiktok"),
        ("dailymotion.com", "dailymotion"),
        ("mixcloud.com", "mixcloud"),
        ("audiomack.com", "audiomack"),
    };

    static bool IsHost(string host, string domain) => host == domain || host.EndsWith("." + domain);

    /// <summary>Returns null if the link is not on a known music/video host.</summary>
    public static LinkInfo? Classify(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;
        var query = HttpUtility.ParseQueryString(uri.Query);

        if (host == "youtu.be")
            return new LinkInfo(url, "youtube", path.Length > 1 ? LinkKind.Track : LinkKind.Unsupported);

        if (IsHost(host, "youtube.com") || IsHost(host, "youtube-nocookie.com"))
        {
            if (path.StartsWith("/shorts/")) return new LinkInfo(url, "youtube shorts", LinkKind.Track);
            if (path == "/watch" && !string.IsNullOrEmpty(query["v"])) return new LinkInfo(url, "youtube", LinkKind.Track);
            if (path.StartsWith("/live/") || path.StartsWith("/embed/") || path.StartsWith("/v/"))
                return new LinkInfo(url, "youtube", LinkKind.Track);
            if (path == "/playlist" && !string.IsNullOrEmpty(query["list"]))
                return new LinkInfo(url, "youtube", LinkKind.Playlist);
            return new LinkInfo(url, "youtube", LinkKind.Unsupported);
        }

        if (host == "spotify.link")
            return new LinkInfo(url, "spotify", LinkKind.ShortLink);

        if (IsHost(host, "spotify.com"))
        {
            var m = SpotifyPath.Match(path);
            if (!m.Success) return new LinkInfo(url, "spotify", LinkKind.Unsupported);
            return m.Groups[1].Value switch
            {
                "track" => new LinkInfo(url, "spotify", LinkKind.Track, m.Groups[2].Value),
                "playlist" => new LinkInfo(url, "spotify", LinkKind.Playlist, m.Groups[2].Value),
                _ => new LinkInfo(url, "spotify", LinkKind.Unsupported, m.Groups[2].Value),
            };
        }

        if (host.StartsWith("music.amazon.") || host == "amazon.com" && path.StartsWith("/music"))
            return new LinkInfo(url, "amazon music", LinkKind.Generic);

        foreach (var (domain, source) in GenericHosts)
            if (IsHost(host, domain))
                return new LinkInfo(url, source, LinkKind.Generic);

        return null;
    }
}
