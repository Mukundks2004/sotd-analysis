using System.Text.RegularExpressions;
using System.Web;

namespace Sotd.Importer;

public enum LinkKind
{
    /// <summary>A single song/video.</summary>
    Track,
    /// <summary>A playlist; each song in it is one entry.</summary>
    Playlist,
    /// <summary>A podcast episode, which is an entry in its own right.</summary>
    Episode,
    /// <summary>A known music/video host; let yt-dlp / page metadata work out what it is.</summary>
    Generic,
    /// <summary>Short link that must be redirected before it can be classified.</summary>
    ShortLink,
    /// <summary>Known host, but a kind of page we cannot turn into entries (album, channel, artist...).</summary>
    Unsupported,
}

/// <param name="ProviderId">
/// The host's own id for the track or playlist (Spotify track id, Apple Music track id, Deezer track id),
/// when the URL carries one. This is what lets us ask the host's API directly instead of guessing.
/// </param>
public record LinkInfo(string Url, string Source, LinkKind Kind, string? ProviderId = null);

public static class LinkClassifier
{
    static readonly Regex SpotifyPath = new(
        @"^/(?:intl-[a-z\-]+/)?(track|playlist|album|artist|episode|show)/([A-Za-z0-9]+)", RegexOptions.Compiled);

    // music.apple.com/us/album/some-song/1234567890 — the album id is the last path segment.
    static readonly Regex AppleAlbumPath = new(
        @"^/(?:[a-z]{2}/)?album/[^/]*/?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // music.apple.com/us/song/some-song/1234567890 and /music-video/...
    static readonly Regex AppleSongPath = new(
        @"^/(?:[a-z]{2}/)?(?:song|music-video)/[^/]*/?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex DeezerPath = new(
        @"^/(?:[a-z]{2}/)?(track|album|playlist|artist)/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // (domain suffix, source name). Matches the host itself or any subdomain.
    static readonly (string Domain, string Source)[] GenericHosts =
    {
        ("tidal.com", "tidal"),
        ("soundcloud.com", "soundcloud"),
        ("snd.sc", "soundcloud"),
        ("bandcamp.com", "bandcamp"),
        ("vimeo.com", "vimeo"),
        ("shazam.com", "shazam"),
        ("tiktok.com", "tiktok"),
        ("dailymotion.com", "dailymotion"),
        ("mixcloud.com", "mixcloud"),
        ("audiomack.com", "audiomack"),
    };

    /// <summary>
    /// Link shorteners and "smart links". These say nothing about the destination, so they have to be
    /// followed before the link can be classified. Without this, a shortened song link looks like a
    /// non-music link and is silently dropped.
    /// </summary>
    static readonly string[] ShortLinkHosts =
    {
        "spotify.link", "spoti.fi", "apple.co", "amzn.to", "deezer.page.link",
        "bit.ly", "t.co", "tinyurl.com", "ow.ly", "buff.ly", "rb.gy", "cutt.ly", "shorturl.at", "goo.gl",
        "lnk.to", "ffm.to", "smarturl.it", "song.link", "album.link", "fanlink.to", "li.sten.to", "linktr.ee",
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

        foreach (var shortHost in ShortLinkHosts)
            if (IsHost(host, shortHost))
                return new LinkInfo(url, "other", LinkKind.ShortLink);

        if (host == "youtu.be")
            return new LinkInfo(url, "youtube", path.Length > 1 ? LinkKind.Track : LinkKind.Unsupported);

        if (IsHost(host, "youtube.com") || IsHost(host, "youtube-nocookie.com"))
        {
            // YouTube Music carries proper track/artist/album tags, so it is worth telling apart.
            var source = host == "music.youtube.com" ? "youtube music" : "youtube";
            if (path.StartsWith("/shorts/")) return new LinkInfo(url, "youtube shorts", LinkKind.Track);
            if (path == "/watch" && !string.IsNullOrEmpty(query["v"])) return new LinkInfo(url, source, LinkKind.Track);
            if (path.StartsWith("/live/") || path.StartsWith("/embed/") || path.StartsWith("/v/"))
                return new LinkInfo(url, source, LinkKind.Track);
            if (path == "/playlist" && !string.IsNullOrEmpty(query["list"]))
                return new LinkInfo(url, source, LinkKind.Playlist);
            return new LinkInfo(url, source, LinkKind.Unsupported);
        }

        if (IsHost(host, "spotify.com"))
        {
            var m = SpotifyPath.Match(path);
            if (!m.Success) return new LinkInfo(url, "spotify", LinkKind.Unsupported);
            return m.Groups[1].Value switch
            {
                "track" => new LinkInfo(url, "spotify", LinkKind.Track, m.Groups[2].Value),
                "playlist" => new LinkInfo(url, "spotify", LinkKind.Playlist, m.Groups[2].Value),
                "episode" => new LinkInfo(url, "spotify", LinkKind.Episode, m.Groups[2].Value),
                _ => new LinkInfo(url, "spotify", LinkKind.Unsupported, m.Groups[2].Value),
            };
        }

        if (IsHost(host, "music.apple.com") || IsHost(host, "itunes.apple.com"))
            return ClassifyApple(url, path, query);

        if (IsHost(host, "deezer.com"))
        {
            var m = DeezerPath.Match(path);
            if (!m.Success) return new LinkInfo(url, "deezer", LinkKind.Unsupported);
            return m.Groups[1].Value.ToLowerInvariant() switch
            {
                "track" => new LinkInfo(url, "deezer", LinkKind.Track, m.Groups[2].Value),
                "playlist" => new LinkInfo(url, "deezer", LinkKind.Playlist, m.Groups[2].Value),
                _ => new LinkInfo(url, "deezer", LinkKind.Unsupported, m.Groups[2].Value),
            };
        }

        if (host.StartsWith("music.amazon.") || host == "amazon.com" && path.StartsWith("/music"))
            return new LinkInfo(url, "amazon music", LinkKind.Generic);

        if (IsHost(host, "soundcloud.com") && path.Contains("/sets/"))
            return new LinkInfo(url, "soundcloud", LinkKind.Playlist);

        foreach (var (domain, source) in GenericHosts)
            if (IsHost(host, domain))
                return new LinkInfo(url, source, LinkKind.Generic);

        return null;
    }

    /// <summary>
    /// An Apple Music song link is an *album* URL with the track in `?i=`. Pulling that id out lets the
    /// importer ask the iTunes lookup API for the exact track instead of scraping the album page, which
    /// is what used to make the album name end up in the song title.
    /// </summary>
    static LinkInfo ClassifyApple(string url, string path, System.Collections.Specialized.NameValueCollection query)
    {
        var trackId = query["i"];
        if (!string.IsNullOrEmpty(trackId) && trackId.All(char.IsDigit))
            return new LinkInfo(url, "apple music", LinkKind.Track, trackId);

        var song = AppleSongPath.Match(path);
        if (song.Success) return new LinkInfo(url, "apple music", LinkKind.Track, song.Groups[1].Value);

        // itunes.apple.com/.../id1234567890 style links
        var legacy = Regex.Match(path, @"/id(\d+)", RegexOptions.IgnoreCase);
        if (legacy.Success && path.Contains("/album/", StringComparison.OrdinalIgnoreCase))
            return new LinkInfo(url, "apple music", LinkKind.Unsupported, legacy.Groups[1].Value);

        var album = AppleAlbumPath.Match(path);
        if (album.Success) return new LinkInfo(url, "apple music", LinkKind.Unsupported, album.Groups[1].Value);

        return new LinkInfo(url, "apple music", LinkKind.Unsupported);
    }
}
