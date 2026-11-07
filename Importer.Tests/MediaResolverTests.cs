using Moq;
using Sotd.Importer;

namespace Importer.Tests;

/// <summary>The pure scoring rules that decide whether a search result is really the posted song.</summary>
[TestFixture]
public class MediaResolverScoringTests
{
    static SpotifyTrack Creep(int seconds = 238) =>
        new("1", "Creep", new() { new MediaArtist("Radiohead", "r1") }, seconds, null);

    [Test]
    public void SearchResultPlausibility()
    {
        var track = Creep();
        Assert.That(MediaResolver.IsPlausible(track, "Radiohead - Creep (Official Video)", "Creep", new() { "Radiohead" }), Is.True);
        Assert.That(MediaResolver.IsPlausible(track, "Some Other Song", "Some Other Song", new() { "Someone" }), Is.False);
        Assert.That(MediaResolver.IsPlausible(track, "Creep (cover)", "Creep", new() { "Random Guy" }), Is.False);
    }

    [Test]
    public void Score_RejectsResultsWhoseLengthIsNothingLikeThePostedMedia()
    {
        var track = Creep();
        var hints = new List<string> { "Radiohead" };
        Assert.That(MediaResolver.Score(track, "Radiohead - Creep (Live at Glastonbury)", "Creep", hints, 600), Is.LessThan(0));
        Assert.That(MediaResolver.Score(track, "Radiohead - Creep", "Creep", hints, 240), Is.GreaterThan(0.9));
    }

    [Test]
    public void Score_PrefersTheResultWithTheCloserDuration()
    {
        var hints = new List<string> { "Radiohead" };
        Assert.That(MediaResolver.Score(Creep(), "Radiohead - Creep", "Creep", hints, 238),
            Is.GreaterThan(MediaResolver.Score(Creep(300), "Radiohead - Creep", "Creep", hints, 238)));
    }

    [TestCase("https://open.spotify.com/track/abc?si=123&utm_source=copy-link", "https://open.spotify.com/track/abc")]
    [TestCase("https://www.youtube.com/watch?v=abc&t=42s&feature=share", "https://www.youtube.com/watch?v=abc")]
    public void CacheKey_IgnoresTrackingParametersSoOneLinkResolvesOnce(string url, string equivalent) =>
        Assert.That(MediaResolver.CacheKey(url), Is.EqualTo(MediaResolver.CacheKey(equivalent)));
}

/// <summary>
/// The resolver with every service it talks to replaced by a mock, so the routing between the providers
/// can be checked without a network.
/// </summary>
[TestFixture]
public class MediaResolverTests
{
    Mock<ISpotifyClient> _spotify = null!;
    Mock<IYtDlpClient> _ytdlp = null!;
    Mock<IPageMetadataClient> _page = null!;
    Mock<IITunesClient> _itunes = null!;
    Mock<IDeezerClient> _deezer = null!;
    Mock<ILinkClassifier> _classifier = null!;
    Mock<IFileMetadataReader> _files = null!;
    Mock<IReviewLog> _log = null!;
    MediaResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _spotify = new Mock<ISpotifyClient>(MockBehavior.Strict);
        _ytdlp = new Mock<IYtDlpClient>(MockBehavior.Strict);
        _page = new Mock<IPageMetadataClient>(MockBehavior.Strict);
        _itunes = new Mock<IITunesClient>(MockBehavior.Strict);
        _deezer = new Mock<IDeezerClient>(MockBehavior.Strict);
        _classifier = new Mock<ILinkClassifier>(MockBehavior.Strict);
        _files = new Mock<IFileMetadataReader>(MockBehavior.Strict);
        _log = new Mock<IReviewLog>();
        _resolver = new MediaResolver(_spotify.Object, _ytdlp.Object, _page.Object, _itunes.Object,
            _deezer.Object, _classifier.Object, _files.Object, new HttpClient(), _log.Object);
    }

    void Classifies(string url, LinkInfo info) => _classifier.Setup(c => c.Classify(url)).Returns(info);

    [Test]
    public async Task UnknownHost_IsLoggedAsSkippedRatherThanGuessedAt()
    {
        _classifier.Setup(c => c.Classify("https://example.com/x")).Returns((LinkInfo?)null);

        var result = await _resolver.ResolveLinkAsync("https://example.com/x", "ctx");

        Assert.That(result, Is.Empty);
        _log.Verify(l => l.SkippedLink("ctx", "https://example.com/x"), Times.Once);
    }

    [Test]
    public async Task SpotifyTrackLink_IsTakenStraightFromTheCatalogue()
    {
        const string url = "https://open.spotify.com/track/t1";
        Classifies(url, new LinkInfo(url, "spotify", LinkKind.Track, "t1"));
        _spotify.Setup(s => s.GetTrackAsync("t1")).ReturnsAsync(
            new SpotifyTrack("t1", "Creep", new() { new MediaArtist("Radiohead", "r1") }, 238, null, "GBAYE9200051"));

        var media = (await _resolver.ResolveLinkAsync(url, "ctx")).Single();

        Assert.That(media.Title, Is.EqualTo("Creep"));
        Assert.That(media.SpotifyTrackId, Is.EqualTo("t1"));
        Assert.That(media.Isrc, Is.EqualTo("GBAYE9200051"));
        Assert.That(media.Quality, Is.EqualTo(MatchQuality.SpotifyExact));
    }

    [Test]
    public async Task SpotifyEpisodeLink_ResolvesAsAnEpisodeRatherThanATrack()
    {
        const string url = "https://open.spotify.com/episode/e1";
        Classifies(url, new LinkInfo(url, "spotify", LinkKind.Episode, "e1"));
        _spotify.Setup(s => s.GetEpisodeAsync("e1")).ReturnsAsync(
            new SpotifyTrack("e1", "Episode 42", new() { new MediaArtist("Song Exploder", null) }, 3600, null,
                null, SpotifyItemKind.Episode));

        var media = (await _resolver.ResolveLinkAsync(url, "ctx")).Single();

        Assert.That(media.Title, Is.EqualTo("Episode 42"));
        Assert.That(media.Link, Is.EqualTo(url));
        _spotify.Verify(s => s.GetTrackAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task SpotifyPlaylist_KeepsLocalFilesButGivesThemNoLink()
    {
        const string url = "https://open.spotify.com/playlist/p1";
        Classifies(url, new LinkInfo(url, "spotify", LinkKind.Playlist, "p1"));
        _spotify.Setup(s => s.GetPlaylistTracksAsync("p1")).ReturnsAsync(new List<SpotifyTrack>
        {
            new("t1", "Creep", new() { new MediaArtist("Radiohead", "r1") }, 238, null),
            new(null, "Demo Take 3", new() { new MediaArtist("My Band", null) }, 125, null, null, SpotifyItemKind.LocalFile),
        });

        var result = await _resolver.ResolveLinkAsync(url, "ctx");

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Link, Is.EqualTo("https://open.spotify.com/track/t1"));
        Assert.That(result[1].Link, Is.Null);
        // A local file's tags are the owner's own, so it goes in the review log rather than being trusted.
        Assert.That(result[1].Quality, Is.EqualTo(MatchQuality.Raw));
    }

    [Test]
    public async Task DeezerTrack_IsTrustedButStillMatchedToSpotifyByIsrc()
    {
        const string url = "https://www.deezer.com/track/3135556";
        Classifies(url, new LinkInfo(url, "deezer", LinkKind.Track, "3135556"));
        _deezer.Setup(d => d.GetTrackAsync("3135556")).ReturnsAsync(
            new ProviderTrack("Harder Better Faster Stronger", new() { "Daft Punk" }, 224, null, "GBDUW0000059"));
        _spotify.Setup(s => s.FindByIsrcAsync("GBDUW0000059")).ReturnsAsync(
            new SpotifyTrack("sp1", "Harder, Better, Faster, Stronger",
                new() { new MediaArtist("Daft Punk", "dp1") }, 224, null, "GBDUW0000059"));

        var media = (await _resolver.ResolveLinkAsync(url, "ctx")).Single();

        // An ISRC is an exact identity, so Spotify's spelling becomes the canonical one
        // and the same song posted from Deezer and from Spotify collapses to one row.
        Assert.That(media.Title, Is.EqualTo("Harder, Better, Faster, Stronger"));
        Assert.That(media.Artists.Single().SpotifyId, Is.EqualTo("dp1"));
        Assert.That(media.SpotifyTrackId, Is.EqualTo("sp1"));
        Assert.That(media.Quality, Is.EqualTo(MatchQuality.SpotifyExact));
        _spotify.Verify(s => s.SearchTracksAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task UnsupportedLink_IsLoggedAsUnresolvableAndNothingIsCalled()
    {
        const string url = "https://open.spotify.com/album/a1";
        Classifies(url, new LinkInfo(url, "spotify", LinkKind.Unsupported, "a1"));

        Assert.That(await _resolver.ResolveLinkAsync(url, "ctx"), Is.Empty);

        _log.Verify(l => l.Unresolvable("ctx", url, It.Is<string>(r => r.Contains("unsupported"))), Times.Once);
    }

    [Test]
    public async Task TheSameLinkIsOnlyResolvedOnce()
    {
        const string url = "https://open.spotify.com/track/t1";
        Classifies(url, new LinkInfo(url, "spotify", LinkKind.Track, "t1"));
        Classifies(url + "?si=abc", new LinkInfo(url, "spotify", LinkKind.Track, "t1"));
        _spotify.Setup(s => s.GetTrackAsync("t1")).ReturnsAsync(
            new SpotifyTrack("t1", "Creep", new() { new MediaArtist("Radiohead", "r1") }, 238, null));

        await _resolver.ResolveLinkAsync(url, "ctx");
        await _resolver.ResolveLinkAsync(url + "?si=abc", "ctx");

        // The share parameter does not change the song, so the second post reuses the first answer.
        _spotify.Verify(s => s.GetTrackAsync("t1"), Times.Once);
    }

    [Test]
    public async Task TheSameLinkPostedTwiceIsOnlyResolvedOnceEvenWhenBothAreInFlightTogether()
    {
        const string url = "https://open.spotify.com/track/t1";
        Classifies(url, new LinkInfo(url, "spotify", LinkKind.Track, "t1"));
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var calls = 0;
        _spotify.Setup(s => s.GetTrackAsync("t1")).Returns(async () =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task;
            return (SpotifyTrack?)new SpotifyTrack("t1", "Creep", new() { new MediaArtist("Radiohead", "r1") }, 238, null);
        });

        var inFlight = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => _resolver.ResolveLinkAsync(url, "ctx"))).ToArray();
        await entered.Task;
        await Task.Delay(50);
        release.SetResult();
        var results = await Task.WhenAll(inFlight);

        // Messages are prepared in parallel, so the cache has to collapse the race rather than let every
        // caller miss and resolve the same link eight times into eight different songs.
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(results, Is.All.SameAs(results[0]));
    }

    [Test]
    public async Task UploadedFile_FallsBackToItsFilenameWhenItHasNoTags()
    {
        _files.Setup(f => f.Read("C:\\tmp\\x.mp3")).Returns((FileMetadata?)null);
        _spotify.Setup(s => s.SearchTracksAsync(It.IsAny<string>())).ReturnsAsync(new List<SpotifyTrack>());

        var media = await _resolver.ResolveFileAsync("C:\\tmp\\x.mp3", "Radiohead - Creep.mp3", "ctx");

        Assert.That(media.Title, Is.EqualTo("Creep"));
        Assert.That(media.Artists.Select(a => a.Name), Does.Contain("Radiohead"));
    }

    [Test]
    public async Task UploadedFile_PrefersItsOwnTagsOverItsFilename()
    {
        _files.Setup(f => f.Read("C:\\tmp\\x.mp3")).Returns(new FileMetadata("Creep", "Radiohead", 238));
        _spotify.Setup(s => s.SearchTracksAsync(It.IsAny<string>())).ReturnsAsync(new List<SpotifyTrack>());

        var media = await _resolver.ResolveFileAsync("C:\\tmp\\x.mp3", "track01.mp3", "ctx");

        Assert.That(media.Title, Is.EqualTo("Creep"));
        Assert.That(media.DurationSeconds, Is.EqualTo(238));
    }
}
