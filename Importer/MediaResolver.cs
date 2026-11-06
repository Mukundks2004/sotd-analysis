using System.Web;

namespace Sotd.Importer;

/// <summary>
/// Turns links and uploaded files into <see cref="ResolvedMedia"/>: title, artists, duration, release date, source.
/// Order of preference: the host's own API (Spotify, Apple Music, Deezer), then source metadata plus a scored
/// Spotify search, then the cleaned raw title and uploader.
/// </summary>
public class MediaResolver
{
    public const string FileUploadSource = "file upload";

    /// <summary>A linked video is allowed to be a little longer than the track (intros, silence at the end).</summary>
    const int DurationToleranceSeconds = 7;
    const double DurationTolerancePercent = 0.06;
    /// <summary>Past this much difference the search result is a different recording, not the posted one.</summary>
    const double DurationRejectRatio = 0.25;
    const double MinimumScore = 0.6;
    const double GoodEnoughScore = 0.95;
    const int MaxSearchQueries = 6;

    readonly SpotifyClient _spotify;
    readonly YtDlpClient _ytdlp;
    readonly PageMetadataClient _page;
    readonly ITunesClient _itunes;
    readonly DeezerClient _deezer;
    readonly HttpClient _http;
    readonly ReviewLog _log;

    /// <summary>
    /// The same link gets posted more than once over five years. Caching keeps the importer from asking the
    /// same questions again and, more importantly, makes repeats resolve to exactly the same song.
    /// </summary>
    readonly Dictionary<string, List<ResolvedMedia>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public MediaResolver(SpotifyClient spotify, YtDlpClient ytdlp, PageMetadataClient page, ITunesClient itunes,
        DeezerClient deezer, HttpClient http, ReviewLog log)
    {
        _spotify = spotify;
        _ytdlp = ytdlp;
        _page = page;
        _itunes = itunes;
        _deezer = deezer;
        _http = http;
        _log = log;
    }

    /// <summary>
    /// Resolves a link into zero or more songs (a playlist gives many). Links on unknown hosts are logged as skipped;
    /// known-host links we cannot read are logged as unresolvable. Never throws for external failures.
    /// </summary>
    public async Task<List<ResolvedMedia>> ResolveLinkAsync(string url, string context)
    {
        var key = CacheKey(url);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var result = await ResolveUncachedAsync(url, context);
        _cache[key] = result;
        return result;
    }

    async Task<List<ResolvedMedia>> ResolveUncachedAsync(string url, string context)
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
                info = await FollowShortLinkAsync(url, context);
                if (info == null) return new();
            }

            List<ResolvedMedia> result;
            if (info.Kind == LinkKind.Unsupported) result = new();
            else if (info.Source == "spotify") result = await ResolveSpotifyAsync(info);
            else if (info.Source == "apple music") result = await ResolveAppleAsync(info, context);
            else if (info.Source == "deezer") result = await ResolveDeezerAsync(info, context);
            else result = await ResolveViaYtDlpAsync(info, context);

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
        var artists = meta?.Artist != null ? Text.SplitArtistNames(meta.Artist) : new List<string>();
        return await BuildFromRawAsync(
            new RawMedia(title, artists, null, meta?.DurationSeconds, null), FileUploadSource, link: null, context);
    }

    /// <summary>
    /// Drops tracking parameters so the same song shared twice hits the cache. Spotify's "?si=", YouTube's
    /// "?t=" and the usual utm_ tags all describe the share, not the song.
    /// </summary>
    public static string CacheKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url.Trim().ToLowerInvariant();
        var query = HttpUtility.ParseQueryString(uri.Query);
        string[] noise = { "si", "t", "start_radio", "feature", "context", "nd", "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "pt", "ls", "app" };
        foreach (var name in noise) query.Remove(name);
        var kept = string.Join("&", query.AllKeys.Where(k => k != null).OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => $"{k}={query[k]}"));
        var path = uri.AbsolutePath.TrimEnd('/');
        return (uri.Host + path + (kept.Length > 0 ? "?" + kept : "")).ToLowerInvariant();
    }

    /// <summary>Follows a shortened link to whatever it really points at.</summary>
    async Task<LinkInfo?> FollowShortLinkAsync(string url, string context)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        var final = resp.RequestMessage?.RequestUri?.ToString();
        var info = final == null ? null : LinkClassifier.Classify(final);
        if (info == null || info.Kind == LinkKind.ShortLink)
        {
            // A shortener pointing somewhere that is not a music host is just a normal non-music link.
            _log.SkippedLink(context, $"{url} -> {final ?? "(no redirect)"}");
            return null;
        }
        return info;
    }

    // ---------- Spotify ----------

    async Task<List<ResolvedMedia>> ResolveSpotifyAsync(LinkInfo info)
    {
        if (info.ProviderId == null) return new();
        if (info.Kind == LinkKind.Track)
        {
            var track = await _spotify.GetTrackAsync(info.ProviderId);
            return track == null ? new() : new() { FromSpotify(track, info.Url) };
        }
        var tracks = await _spotify.GetPlaylistTracksAsync(info.ProviderId);
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
        Isrc = t.Isrc,
        Quality = MatchQuality.SpotifyExact,
    };

    // ---------- Apple Music / Deezer ----------

    async Task<List<ResolvedMedia>> ResolveAppleAsync(LinkInfo info, string context)
    {
        if (info.ProviderId != null && await _itunes.GetTrackAsync(info.ProviderId) is { } track)
            return new() { await BuildFromProviderAsync(track, info.Source, info.Url, context) };

        // No usable track id (or the lookup failed): fall back to the page itself.
        if (await _page.GetAsync(info.Url, info.ProviderId) is { } page)
        {
            var raw = new RawMedia(page.Title, page.Artist != null ? Text.SplitArtistNames(page.Artist) : new(),
                null, page.DurationSeconds, page.Date);
            return new() { await BuildFromRawAsync(raw, info.Source, info.Url, context) };
        }
        return new();
    }

    async Task<List<ResolvedMedia>> ResolveDeezerAsync(LinkInfo info, string context)
    {
        if (info.ProviderId == null) return new();
        if (info.Kind == LinkKind.Track)
        {
            var track = await _deezer.GetTrackAsync(info.ProviderId);
            return track == null ? new() : new() { await BuildFromProviderAsync(track, info.Source, info.Url, context) };
        }

        var tracks = await _deezer.GetPlaylistTracksAsync(info.ProviderId);
        var result = new List<ResolvedMedia>();
        foreach (var track in tracks)
            result.Add(await BuildFromProviderAsync(track, info.Source, info.Url, context));
        return result;
    }

    /// <summary>
    /// Builds an entry from a host API that already gave an exact answer. The title, artists and duration are
    /// trusted; Spotify is only consulted to attach ids (and therefore genres), by ISRC where possible.
    /// </summary>
    async Task<ResolvedMedia> BuildFromProviderAsync(ProviderTrack track, string source, string? link, string context)
    {
        var media = new ResolvedMedia
        {
            SourceName = source,
            Link = link,
            Title = track.Title,
            Artists = track.Artists.Select(a => new MediaArtist(a, null)).ToList(),
            DurationSeconds = track.DurationSeconds ?? 0,
            ReleaseDate = track.ReleaseDate,
            Isrc = track.Isrc,
            Quality = MatchQuality.ProviderExact,
        };

        SpotifyTrack? match = null;
        if (track.Isrc != null)
        {
            match = await _spotify.FindByIsrcAsync(track.Isrc);
            if (match != null) media.Quality = MatchQuality.SpotifyExact;
        }
        if (match == null)
        {
            var candidates = new List<SearchCandidate> { new(track.Title, track.Artists) };
            var scored = await SearchSpotifyAsync(track.Title, candidates, track.DurationSeconds);
            if (scored != null)
            {
                match = scored.Value.Track;
                media.Quality = MatchQuality.SpotifySearch;
            }
        }

        if (match != null)
        {
            media.Title = match.Name;
            media.Artists = match.Artists;
            media.SpotifyTrackId = match.Id;
            media.Isrc ??= match.Isrc;
            media.ReleaseDate ??= match.ReleaseDate;
        }

        if (media.DurationSeconds == 0) _log.ZeroDuration(context, $"{media} {link}");
        return media;
    }

    // ---------- yt-dlp / page metadata ----------

    async Task<List<ResolvedMedia>> ResolveViaYtDlpAsync(LinkInfo info, string context)
    {
        var items = await _ytdlp.GetAsync(info.Url);
        if (items.Count == 0)
        {
            // Hosts yt-dlp cannot read (Tidal, Amazon Music, ...): try the page's own metadata.
            if (info.Kind is LinkKind.Generic or LinkKind.Track && await _page.GetAsync(info.Url) is { } page)
            {
                var raw = new RawMedia(page.Title, page.Artist != null ? Text.SplitArtistNames(page.Artist) : new(),
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

            var uploader = item.Uploader != null ? Text.CleanUploader(item.Uploader) : null;
            var artists = item.Artists.Count > 0
                ? item.Artists
                : uploader is { Length: > 0 } ? new List<string> { uploader } : new List<string>();
            var raw = new RawMedia(item.Title!, artists, item.Track,
                item.Duration is { } d ? (int)Math.Round(d) : null, item.Date,
                ArtistsFromUploader: item.Artists.Count == 0);
            var link = isCollection ? item.Url ?? info.Url : info.Url;
            result.Add(await BuildFromRawAsync(raw, info.Source, link, context));
        }
        return result;
    }

    // ---------- shared: raw metadata -> Spotify search -> fallback ----------

    /// <param name="ArtistsFromUploader">
    /// True when the only artist information is the channel that uploaded it, which is often a label or a
    /// compilation channel rather than the artist.
    /// </param>
    record RawMedia(
        string Title,
        List<string> Artists,
        string? TrackName,
        int? DurationSeconds,
        DateTime? Date,
        bool ArtistsFromUploader = false);

    record SearchCandidate(string Title, List<string> Artists);

    async Task<ResolvedMedia> BuildFromRawAsync(RawMedia raw, string source, string? link, string context)
    {
        var media = new ResolvedMedia
        {
            SourceName = source,
            Link = link,
            DurationSeconds = raw.DurationSeconds ?? 0,
            ReleaseDate = raw.Date,
        };

        var cleanTitle = Text.CleanVideoTitle(raw.Title);
        if (cleanTitle.Length == 0) cleanTitle = raw.Title.Trim();
        var (titleWithoutFeat, featured) = Text.SplitFeatured(cleanTitle);

        var candidates = BuildSearchCandidates(raw, titleWithoutFeat, featured);
        var match = await SearchSpotifyAsync(raw.Title, candidates, raw.DurationSeconds);

        if (match != null)
        {
            media.Title = match.Value.Track.Name;
            media.Artists = match.Value.Track.Artists;
            media.SpotifyTrackId = match.Value.Track.Id;
            media.Isrc = match.Value.Track.Isrc;
            media.ReleaseDate ??= match.Value.Track.ReleaseDate;
            media.Quality = MatchQuality.SpotifySearch;
        }
        else
        {
            var (title, artists) = BestGuessWithoutSpotify(raw, titleWithoutFeat, featured);
            media.Title = title;
            media.Artists = artists.Select(a => new MediaArtist(a, null)).ToList();
            media.Quality = MatchQuality.Raw;
        }

        if (media.DurationSeconds == 0)
            _log.ZeroDuration(context, $"{media} {link}");
        return media;
    }

    /// <summary>
    /// Every sensible reading of the raw metadata, best first. "A - B - C" is genuinely ambiguous, so both
    /// splits are offered and whichever one Spotify actually has wins.
    /// </summary>
    static List<SearchCandidate> BuildSearchCandidates(RawMedia raw, string titleWithoutFeat, List<string> featured)
    {
        var candidates = new List<SearchCandidate>();
        var taggedArtists = raw.ArtistsFromUploader ? new List<string>() : raw.Artists;
        var hints = Combine(taggedArtists, featured);

        void Add(string title, IEnumerable<string> artists)
        {
            title = title.Trim();
            if (title.Length == 0) return;
            var list = Combine(artists.ToList(), featured);
            if (!candidates.Any(c => c.Title == title && c.Artists.SequenceEqual(list)))
                candidates.Add(new SearchCandidate(title, list));
        }

        // Explicit track/artist tags (YouTube Music, "- Topic" uploads, file tags) are the best input there is.
        if (!string.IsNullOrWhiteSpace(raw.TrackName)) Add(raw.TrackName!, hints.Count > 0 ? hints : raw.Artists);

        foreach (var (artist, title) in Text.ArtistTitleCandidates(titleWithoutFeat))
            Add(title, new[] { artist });

        Add(titleWithoutFeat, hints);
        Add(titleWithoutFeat, Array.Empty<string>());
        return candidates;
    }

    /// <summary>
    /// What to store when Spotify has nothing. The cleaned title beats the raw video title, and an
    /// "Artist - Title" split beats using the uploading channel as the artist.
    /// </summary>
    static (string Title, List<string> Artists) BestGuessWithoutSpotify(
        RawMedia raw, string titleWithoutFeat, List<string> featured)
    {
        if (!string.IsNullOrWhiteSpace(raw.TrackName) && raw.Artists.Count > 0 && !raw.ArtistsFromUploader)
            return (raw.TrackName!.Trim(), Combine(raw.Artists, featured));

        if (!raw.ArtistsFromUploader && raw.Artists.Count > 0)
            return (titleWithoutFeat, Combine(raw.Artists, featured));

        // Only a channel name to go on: "Artist - Title" in the video title is the better guess.
        var split = Text.ArtistTitleCandidates(titleWithoutFeat).FirstOrDefault();
        if (split.Artist is { Length: > 0 } && !Text.IsLikelyChannelName(split.Artist))
            return (split.Title, Combine(new List<string> { split.Artist }, featured));

        var uploader = raw.Artists.Where(a => !Text.IsLikelyChannelName(a)).ToList();
        return (titleWithoutFeat, Combine(uploader, featured));
    }

    static List<string> Combine(List<string> first, List<string> second)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var name in first.Concat(second))
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name.Trim())) result.Add(name.Trim());
        return result;
    }

    /// <summary>
    /// Searches Spotify for each candidate reading and returns the single best-scoring track, or null when
    /// nothing scores well enough. Scoring everything beats taking the first result that looks acceptable,
    /// because Spotify happily returns a cover or a live version ahead of the real thing.
    /// </summary>
    async Task<(SpotifyTrack Track, double Score)?> SearchSpotifyAsync(
        string rawTitle, List<SearchCandidate> candidates, int? durationSeconds)
    {
        var queries = new List<(string Query, SearchCandidate Candidate)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddQuery(string query, SearchCandidate candidate)
        {
            if (string.IsNullOrWhiteSpace(query) || !seen.Add(query)) return;
            queries.Add((query, candidate));
        }

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Title)) continue;
            var artist = candidate.Artists.FirstOrDefault();
            if (artist != null)
            {
                AddQuery($"track:\"{candidate.Title}\" artist:\"{artist}\"", candidate);
                AddQuery($"{artist} {candidate.Title}", candidate);
            }
            else
            {
                AddQuery(candidate.Title, candidate);
            }
        }

        (SpotifyTrack Track, double Score)? best = null;
        foreach (var (query, candidate) in queries.Take(MaxSearchQueries))
        {
            foreach (var track in await _spotify.SearchTracksAsync(query))
            {
                var score = Score(track, rawTitle, candidate.Title, candidate.Artists, durationSeconds);
                if (score >= MinimumScore && (best == null || score > best.Value.Score))
                    best = (track, score);
            }
            if (best is { Score: >= GoodEnoughScore }) break;
        }
        return best;
    }

    /// <summary>
    /// How much a search result looks like the thing that was actually posted, from 0 to 1, or a negative
    /// number when it is definitely wrong. Duration is the signal every source agrees on, so a result whose
    /// length is nothing like the posted media is rejected outright: that is what stops a three minute studio
    /// track being matched to a ten minute live upload.
    /// </summary>
    public static double Score(SpotifyTrack track, string rawTitle, string searchTitle, List<string> artistHints,
        int? durationSeconds)
    {
        var trackNorm = Text.NormalizeForMatching(track.Name);
        if (trackNorm.Length < 2) return -1;
        var rawNorm = Text.NormalizeForMatching(rawTitle);
        var searchNorm = Text.NormalizeForMatching(searchTitle);

        var titleScore = Text.ContainsAsTokens(rawNorm, trackNorm) || Text.ContainsAsTokens(searchNorm, trackNorm)
                         || Text.ContainsAsTokens(trackNorm, searchNorm)
            ? 1.0
            : Math.Max(Text.TokenSimilarity(searchNorm, trackNorm), Text.TokenSimilarity(rawNorm, trackNorm));
        if (titleScore < 0.34) return -1;

        var artistScore = ArtistScore(track, rawNorm, artistHints);
        // With artist information in hand, a result crediting nobody we recognise is the wrong recording.
        if (artistHints.Count > 0 && artistScore < 0.34) return -1;

        var durationScore = 0.6;
        if (durationSeconds is > 0 && track.DurationSeconds > 0)
        {
            var delta = Math.Abs(durationSeconds.Value - track.DurationSeconds);
            var tolerance = Math.Max(DurationToleranceSeconds, track.DurationSeconds * DurationTolerancePercent);
            if (delta <= tolerance)
            {
                durationScore = 1.0;
            }
            else
            {
                var ratio = delta / (double)track.DurationSeconds;
                if (ratio > DurationRejectRatio) return -1;
                durationScore = 1.0 - ratio / DurationRejectRatio;
            }
        }

        return 0.45 * titleScore + 0.30 * artistScore + 0.25 * durationScore;
    }

    static double ArtistScore(SpotifyTrack track, string rawNorm, List<string> artistHints)
    {
        if (track.Artists.Count == 0) return 0;
        var best = 0.0;
        foreach (var artist in track.Artists)
        {
            var artistNorm = Text.NormalizeTitle(artist.Name);
            if (artistNorm.Length == 0) continue;

            // The artist being named anywhere in the posted title is good evidence on its own.
            if (Text.ContainsAsTokens(rawNorm, artistNorm)) best = Math.Max(best, 0.9);

            foreach (var hint in artistHints)
            {
                var hintNorm = Text.NormalizeTitle(hint);
                if (hintNorm.Length == 0) continue;
                if (Text.ContainsAsTokens(hintNorm, artistNorm) || Text.ContainsAsTokens(artistNorm, hintNorm))
                    best = Math.Max(best, 1.0);
                else
                    best = Math.Max(best, Text.TokenSimilarity(hintNorm, artistNorm));
            }
        }
        return artistHints.Count == 0 ? Math.Max(best, 0.4) : best;
    }

    /// <summary>Guards against Spotify's search returning something unrelated to what was posted.</summary>
    public static bool IsPlausible(SpotifyTrack t, string rawTitle, string searchTitle, List<string> artists) =>
        Score(t, rawTitle, searchTitle, artists, null) >= MinimumScore;
}
