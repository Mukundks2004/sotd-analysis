namespace Sotd.Importer;

/// <summary>
/// Turns links and uploaded files into <see cref="ResolvedMedia"/>: title, artists, duration, release date, source.
/// Order of preference: Spotify directly, then source metadata + Spotify search, then the raw title/uploader.
/// </summary>
public class MediaResolver
{
    public const string FileUploadSource = "file upload";

    readonly SpotifyClient _spotify;
    readonly YtDlpClient _ytdlp;
    readonly PageMetadataClient _page;
    readonly HttpClient _http;
    readonly ReviewLog _log;

    public MediaResolver(SpotifyClient spotify, YtDlpClient ytdlp, PageMetadataClient page, HttpClient http, ReviewLog log)
    {
        _spotify = spotify;
        _ytdlp = ytdlp;
        _page = page;
        _http = http;
        _log = log;
    }

    /// <summary>
    /// Resolves a link into zero or more songs (a playlist gives many). Links on unknown hosts are logged as skipped;
    /// known-host links we cannot read are logged as unresolvable. Never throws for external failures.
    /// </summary>
    public async Task<List<ResolvedMedia>> ResolveLinkAsync(string url, string context)
    {
        try
        {
            var info = LinkClassifier.Classify(url);
            if (info == null)
            {
                _log.SkippedLink(context, url);
                return new();
            }

            if (info.Kind == LinkKind.ShortLink)
            {
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                var final = resp.RequestMessage?.RequestUri?.ToString();
                info = final == null ? null : LinkClassifier.Classify(final);
                if (info == null)
                {
                    _log.Unresolvable(context, url, "short link did not redirect to a known music host");
                    return new();
                }
            }

            List<ResolvedMedia> result = info.Kind switch
            {
                LinkKind.Unsupported => new(),
                _ when info.Source == "spotify" => await ResolveSpotifyAsync(info),
                _ => await ResolveViaYtDlpAsync(info, context),
            };

            if (result.Count == 0)
                _log.Unresolvable(context, url, info.Kind == LinkKind.Unsupported
                    ? "unsupported kind of link (album, channel, artist, ...)"
                    : "could not read the link (deleted, private, or unsupported site)");
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or InvalidOperationException)
        {
            _log.Unresolvable(context, url, ex.Message);
            return new();
        }
    }

    /// <summary>Resolves an uploaded audio/video file that has already been downloaded to <paramref name="localPath"/>.</summary>
    public async Task<ResolvedMedia> ResolveFileAsync(string localPath, string filename, string context)
    {
        var meta = FileMetadataReader.Read(localPath);
        var title = meta?.Title ?? Path.GetFileNameWithoutExtension(filename);
        var artists = meta?.Artist != null ? new List<string> { meta.Artist } : new List<string>();
        var media = await BuildFromRawAsync(new RawMedia(title, artists, null, meta?.DurationSeconds, null),
            FileUploadSource, link: null, context);
        return media;
    }

    // ---------- Spotify ----------

    async Task<List<ResolvedMedia>> ResolveSpotifyAsync(LinkInfo info)
    {
        if (info.SpotifyId == null) return new();
        if (info.Kind == LinkKind.Track)
        {
            var track = await _spotify.GetTrackAsync(info.SpotifyId);
            return track == null ? new() : new() { FromSpotify(track, info.Url) };
        }
        var tracks = await _spotify.GetPlaylistTracksAsync(info.SpotifyId);
        return tracks.Select(t => FromSpotify(t, $"https://open.spotify.com/track/{t.Id}")).ToList();
    }

    static ResolvedMedia FromSpotify(SpotifyTrack t, string link) => new()
    {
        SourceName = "spotify",
        Link = link,
        Title = t.Name,
        Artists = t.Artists,
        DurationSeconds = t.DurationSeconds,
        ReleaseDate = t.ReleaseDate,
        SpotifyTrackId = t.Id,
        Quality = MatchQuality.SpotifyExact,
    };

    // ---------- yt-dlp / page metadata ----------

    async Task<List<ResolvedMedia>> ResolveViaYtDlpAsync(LinkInfo info, string context)
    {
        var items = await _ytdlp.GetAsync(info.Url);
        if (items.Count == 0)
        {
            // Hosts yt-dlp cannot read (Apple Music, Tidal, Amazon Music, ...): try the page's own metadata.
            if (info.Kind == LinkKind.Generic && await _page.GetAsync(info.Url) is { } page)
            {
                var raw = new RawMedia(page.Title, page.Artist != null ? new List<string> { page.Artist } : new List<string>(),
                    null, page.DurationSeconds, page.Date);
                return new() { await BuildFromRawAsync(raw, info.Source, info.Url, context) };
            }
            return new();
        }

        var isCollection = items.Count > 1 || info.Kind == LinkKind.Playlist;
        var result = new List<ResolvedMedia>();
        foreach (var item0 in items)
        {
            var item = item0;
            // Flat playlist entries often have no duration; look the single video up properly.
            if (item.Duration == null && isCollection && item.Url != null)
                item = (await _ytdlp.GetAsync(item.Url)).FirstOrDefault() ?? item;

            if (string.IsNullOrWhiteSpace(item.Title))
            {
                _log.Unresolvable(context, item.Url ?? info.Url, "entry has no title");
                continue;
            }

            var artists = item.Artists.Count > 0 ? item.Artists
                : item.Uploader != null ? new List<string> { Text.CleanUploader(item.Uploader) } : new List<string>();
            var raw = new RawMedia(item.Title!, artists, item.Track, item.Duration is { } d ? (int)Math.Round(d) : null, item.Date);
            var link = isCollection ? item.Url ?? info.Url : info.Url;
            result.Add(await BuildFromRawAsync(raw, info.Source, link, context));
        }
        return result;
    }

    // ---------- shared: raw metadata -> Spotify search -> fallback ----------

    record RawMedia(string Title, List<string> Artists, string? TrackName, int? DurationSeconds, DateTime? Date);

    async Task<ResolvedMedia> BuildFromRawAsync(RawMedia raw, string source, string? link, string context)
    {
        var media = new ResolvedMedia
        {
            SourceName = source,
            Link = link,
            DurationSeconds = raw.DurationSeconds ?? 0,
            ReleaseDate = raw.Date,
        };

        // What to search Spotify for. Prefer explicit track/artist tags, then "Artist - Title", then the cleaned title.
        string searchTitle;
        var searchArtists = raw.Artists;
        if (!string.IsNullOrWhiteSpace(raw.TrackName) && raw.Artists.Count > 0)
        {
            searchTitle = raw.TrackName!;
        }
        else if (Text.TryParseArtistTitle(Text.CleanVideoTitle(raw.Title)) is { } parsed)
        {
            searchTitle = parsed.Title;
            searchArtists = new List<string> { parsed.Artist };
        }
        else
        {
            searchTitle = Text.CleanVideoTitle(raw.Title);
        }

        var match = await SearchSpotifyAsync(raw.Title, searchTitle, searchArtists);
        if (match != null)
        {
            media.Title = match.Name;
            media.Artists = match.Artists;
            media.SpotifyTrackId = match.Id;
            media.Quality = MatchQuality.SpotifySearch;
        }
        else
        {
            media.Title = raw.Title;
            media.Artists = raw.Artists.Select(a => new MediaArtist(a, null)).ToList();
            media.Quality = MatchQuality.Raw;
        }

        if (media.DurationSeconds == 0)
            _log.ZeroDuration(context, $"{media} {link}");
        return media;
    }

    async Task<SpotifyTrack?> SearchSpotifyAsync(string rawTitle, string searchTitle, List<string> artists)
    {
        if (string.IsNullOrWhiteSpace(searchTitle)) return null;
        var artist = artists.FirstOrDefault();
        var queries = new List<string>();
        if (artist != null) queries.Add($"track:\"{searchTitle}\" artist:\"{artist}\"");
        queries.Add(artist != null ? $"{artist} {searchTitle}" : searchTitle);

        foreach (var q in queries.Distinct())
        {
            var results = await _spotify.SearchTracksAsync(q);
            var pick = results.FirstOrDefault(t => IsPlausible(t, rawTitle, searchTitle, artists));
            if (pick != null) return pick;
        }
        return null;
    }

    /// <summary>Guards against Spotify's search returning something unrelated to what was posted.</summary>
    public static bool IsPlausible(SpotifyTrack t, string rawTitle, string searchTitle, List<string> artists)
    {
        var trackNorm = Text.NormalizeTitle(t.Name);
        if (trackNorm.Length < 2) return false;
        var rawNorm = Text.NormalizeTitle(rawTitle);
        var searchNorm = Text.NormalizeTitle(searchTitle);
        var titleOk = rawNorm.Contains(trackNorm) || searchNorm.Contains(trackNorm) || trackNorm.Contains(searchNorm);
        if (!titleOk) return false;
        if (artists.Count == 0) return true;

        var haystack = rawNorm + " " + string.Join(" ", artists.Select(Text.NormalizeTitle));
        return t.Artists.Any(a =>
        {
            var n = Text.NormalizeTitle(a.Name);
            return n.Length > 0 && haystack.Contains(n);
        });
    }
}
