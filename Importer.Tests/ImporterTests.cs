using System.Net;
using Microsoft.EntityFrameworkCore;
using Sotd.Database;
using Sotd.Importer;
using Xunit;

namespace Importer.Tests;

public class TextTests
{
    [Fact]
    public void ExtractLinks_TrimsPunctuationAndDedupes()
    {
        var links = Text.ExtractLinks("check this https://youtu.be/abc123. and (https://open.spotify.com/track/xyz) <https://a.com/x> https://youtu.be/abc123");
        Assert.Equal(new[] { "https://youtu.be/abc123", "https://open.spotify.com/track/xyz", "https://a.com/x" }, links);
    }

    [Fact]
    public void NormalizeTitle_IgnoresCaseAccentsPunctuation() =>
        Assert.Equal("beyonce halo", Text.NormalizeTitle("  Beyoncé -- HALO!! "));

    [Fact]
    public void CleanVideoTitle_StripsNoise() =>
        Assert.Equal("Radiohead - Creep", Text.CleanVideoTitle("Radiohead - Creep (Official Video) [HD]"));

    [Fact]
    public void SanitizeFileName_KeepsExtensionAndRemovesBadChars()
    {
        Assert.Equal("a_b_c.mp3", Text.SanitizeFileName("a:b?c.mp3"));
        var longName = Text.SanitizeFileName(new string('x', 300) + ".flac");
        Assert.EndsWith(".flac", longName);
        Assert.True(longName.Length <= 105);
    }

    [Fact]
    public void TryParseArtistTitle_Splits() =>
        Assert.Equal(("Radiohead", "Creep"), Text.TryParseArtistTitle("Radiohead - Creep"));

    [Theory]
    [InlineData("Radiohead - Creep | Official Music Video", "Radiohead - Creep")]
    [InlineData("Radiohead - Creep - Official Video", "Radiohead - Creep")]
    [InlineData("【MV】Band - Song", "Band - Song")]
    [InlineData("Artist - Song (prod. by Someone)", "Artist - Song")]
    [InlineData("\"Creep\"", "Creep")]
    public void CleanVideoTitle_StripsUnbracketedAndCjkNoise(string raw, string expected) =>
        Assert.Equal(expected, Text.CleanVideoTitle(raw));

    [Fact]
    public void SplitFeatured_PullsOutCredits()
    {
        var (title, featured) = Text.SplitFeatured("Song Name (feat. Alice & Bob)");
        Assert.Equal("Song Name", title);
        Assert.Equal(new[] { "Alice", "Bob" }, featured);
    }

    [Fact]
    public void SplitFeatured_HandlesTrailingFeat()
    {
        var (title, featured) = Text.SplitFeatured("Song Name ft. Alice");
        Assert.Equal("Song Name", title);
        Assert.Equal(new[] { "Alice" }, featured);
    }

    [Fact]
    public void ArtistTitleCandidates_OffersBothSplitsOfAnAmbiguousTitle()
    {
        var candidates = Text.ArtistTitleCandidates("Artist - Song - Live at Wembley");
        Assert.Contains(("Artist", "Song - Live at Wembley"), candidates);
        Assert.Contains(("Artist - Song", "Live at Wembley"), candidates);
    }

    [Theory]
    [InlineData("Artist: Song", "Artist", "Song")]
    [InlineData("Artist \"Song\"", "Artist", "Song")]
    public void ArtistTitleCandidates_HandlesOtherShapes(string raw, string artist, string title) =>
        Assert.Contains((artist, title), Text.ArtistTitleCandidates(raw));

    [Theory]
    [InlineData("RadioheadVEVO", "Radiohead")]
    [InlineData("Radiohead - Topic", "Radiohead")]
    [InlineData("Radiohead Official Channel", "Radiohead")]
    public void CleanUploader_StripsChannelDecoration(string raw, string expected) =>
        Assert.Equal(expected, Text.CleanUploader(raw));

    [Theory]
    [InlineData("Various Artists")]
    [InlineData("Some Label Records")]
    [InlineData("SomethingVEVO")]
    public void IsLikelyChannelName_RejectsNonArtists(string name) => Assert.True(Text.IsLikelyChannelName(name));

    [Theory]
    [InlineData("Radiohead")]
    [InlineData("TV Girl")]
    public void IsLikelyChannelName_KeepsRealArtists(string name) => Assert.False(Text.IsLikelyChannelName(name));

    [Theory]
    [InlineData("Creep - Remastered 2011")]
    [InlineData("Creep (Remastered)")]
    [InlineData("Creep - Radio Edit")]
    [InlineData("Creep (feat. Someone)")]
    public void NormalizeForMatching_IgnoresReissueMarkers(string title) =>
        Assert.Equal(Text.NormalizeForMatching("Creep"), Text.NormalizeForMatching(title));

    [Theory]
    [InlineData("Creep - Live at Wembley")]
    [InlineData("Creep (Remix)")]
    [InlineData("Creep - Acoustic")]
    public void NormalizeForMatching_KeepsDifferentRecordingsApart(string title) =>
        Assert.NotEqual(Text.NormalizeForMatching("Creep"), Text.NormalizeForMatching(title));

    [Fact]
    public void ContainsAsTokens_DoesNotMatchPartialWords()
    {
        Assert.True(Text.ContainsAsTokens("radiohead creep official video", "creep"));
        Assert.False(Text.ContainsAsTokens("homesick blues", "home"));
    }
}

public class LinkClassifierTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", "youtube", LinkKind.Track)]
    [InlineData("https://youtu.be/abc", "youtube", LinkKind.Track)]
    [InlineData("https://music.youtube.com/watch?v=abc", "youtube music", LinkKind.Track)]
    [InlineData("https://www.youtube.com/shorts/abc", "youtube shorts", LinkKind.Track)]
    [InlineData("https://www.youtube.com/playlist?list=PL1", "youtube", LinkKind.Playlist)]
    [InlineData("https://www.youtube.com/@somechannel", "youtube", LinkKind.Unsupported)]
    [InlineData("https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC?si=x", "spotify", LinkKind.Track)]
    [InlineData("https://open.spotify.com/intl-de/track/4uLU6hMCjMI75M1A2tKUQC", "spotify", LinkKind.Track)]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DX", "spotify", LinkKind.Playlist)]
    [InlineData("https://open.spotify.com/episode/512ojhOuo1ktJprKbVcKyQ", "spotify", LinkKind.Episode)]
    [InlineData("https://open.spotify.com/album/abc", "spotify", LinkKind.Unsupported)]
    [InlineData("https://music.apple.com/us/album/x/1?i=2", "apple music", LinkKind.Track)]
    [InlineData("https://music.apple.com/us/song/halo/123456", "apple music", LinkKind.Track)]
    [InlineData("https://music.apple.com/us/album/x/1", "apple music", LinkKind.Unsupported)]
    [InlineData("https://www.deezer.com/us/track/3135556", "deezer", LinkKind.Track)]
    [InlineData("https://www.deezer.com/playlist/1234", "deezer", LinkKind.Playlist)]
    [InlineData("https://soundcloud.com/a/sets/b", "soundcloud", LinkKind.Playlist)]
    [InlineData("https://artist.bandcamp.com/track/x", "bandcamp", LinkKind.Generic)]
    [InlineData("https://soundcloud.com/a/b", "soundcloud", LinkKind.Generic)]
    [InlineData("https://music.amazon.co.uk/albums/B0", "amazon music", LinkKind.Generic)]
    public void Classifies(string url, string source, LinkKind kind)
    {
        var info = LinkClassifier.Classify(url);
        Assert.NotNull(info);
        Assert.Equal(source, info!.Source);
        Assert.Equal(kind, info.Kind);
    }

    [Theory]
    [InlineData("https://twitter.com/a/status/1")]
    [InlineData("https://tenor.com/view/x")]
    [InlineData("https://i.imgur.com/a.png")]
    [InlineData("https://notyoutube.com/watch?v=abc")]
    public void IgnoresOtherHosts(string url) => Assert.Null(LinkClassifier.Classify(url));

    [Theory]
    [InlineData("https://spotify.link/abc")]
    [InlineData("https://bit.ly/abc")]
    [InlineData("https://t.co/abc")]
    [InlineData("https://apple.co/abc")]
    [InlineData("https://lnk.to/abc")]
    public void ShortenersAreFollowedRatherThanDropped(string url) =>
        Assert.Equal(LinkKind.ShortLink, LinkClassifier.Classify(url)!.Kind);
}

public class ParsingTests
{
    [Theory]
    [InlineData("2020-05-17", 2020, 5, 17)]
    [InlineData("2020-05", 2020, 5, 1)]
    [InlineData("1999", 1999, 1, 1)]
    public void SpotifyReleaseDates(string s, int y, int m, int d) =>
        Assert.Equal(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc), SpotifyClient.ParseReleaseDate(s));

    [Fact]
    public void PageMetadata_ReadsJsonLd()
    {
        var html = "<html><script type=\"application/ld+json\">" +
                   "{\"@context\":\"http://schema.org\",\"@type\":\"MusicRecording\",\"name\":\"Halo\"," +
                   "\"duration\":\"PT3M45S\",\"datePublished\":\"2008-11-14\"," +
                   "\"byArtist\":[{\"@type\":\"MusicGroup\",\"name\":\"Beyoncé\"}]}</script></html>";
        var meta = PageMetadataClient.Parse(html);
        Assert.NotNull(meta);
        Assert.Equal("Halo", meta!.Title);
        Assert.Equal("Beyoncé", meta.Artist);
        Assert.Equal(225, meta.DurationSeconds);
    }

    [Fact]
    public void PageMetadata_FallsBackToOpenGraph()
    {
        var meta = PageMetadataClient.Parse("<meta property=\"og:title\" content=\"Halo by Beyoncé on Apple Music\">");
        Assert.Equal("Halo", meta!.Title);
        Assert.Equal("Beyoncé", meta.Artist);
    }

    [Fact]
    public void PageMetadata_IgnoresAlbumNodeSoAlbumNameIsNotStoredAsTheSong()
    {
        var html = "<html><script type=\"application/ld+json\">" +
                   "{\"@type\":\"MusicAlbum\",\"name\":\"Greatest Hits\"," +
                   "\"byArtist\":{\"@type\":\"MusicGroup\",\"name\":\"Beyonc\u00e9\"}}</script></html>";
        Assert.Null(PageMetadataClient.Parse(html));
    }

    [Fact]
    public void PageMetadata_PicksTheRequestedTrackOutOfAnAlbum()
    {
        var html = "<html><script type=\"application/ld+json\">" +
                   "{\"@type\":[\"MusicAlbum\",\"Product\"],\"name\":\"I Am... Sasha Fierce\"," +
                   "\"byArtist\":{\"@type\":\"MusicGroup\",\"name\":\"Beyonc\u00e9\"}," +
                   "\"track\":[{\"@type\":\"MusicRecording\",\"name\":\"Single Ladies\",\"url\":\"https://music.apple.com/us/album/x/1?i=111\"}," +
                   "{\"@type\":\"MusicRecording\",\"name\":\"Halo\",\"url\":\"https://music.apple.com/us/album/x/1?i=222\"}]}" +
                   "</script></html>";
        var meta = PageMetadataClient.Parse(html, "222");
        Assert.Equal("Halo", meta!.Title);
        Assert.Equal("Beyonc\u00e9", meta.Artist);
    }

    [Fact]
    public void ITunes_ParsesLookupResponse()
    {
        var json = "{\"resultCount\":2,\"results\":[" +
                   "{\"wrapperType\":\"collection\",\"collectionName\":\"I Am... Sasha Fierce\"}," +
                   "{\"kind\":\"song\",\"trackName\":\"Halo\",\"artistName\":\"Beyonc\u00e9\"," +
                   "\"trackTimeMillis\":261000,\"releaseDate\":\"2008-11-14T08:00:00Z\"}]}";
        var track = ITunesClient.Parse(json);
        Assert.Equal("Halo", track!.Title);
        Assert.Equal(new[] { "Beyonc\u00e9" }, track.Artists);
        Assert.Equal(261, track.DurationSeconds);
    }

    [Fact]
    public void Deezer_ParsesTrackIncludingIsrc()
    {
        var json = "{\"id\":3135556,\"title\":\"Harder Better Faster Stronger\",\"duration\":224," +
                   "\"isrc\":\"GBDUW0000059\",\"release_date\":\"2001-03-07\"," +
                   "\"artist\":{\"name\":\"Daft Punk\"}}";
        var track = DeezerClient.ParseTrack(json);
        Assert.Equal("Harder Better Faster Stronger", track!.Title);
        Assert.Equal(new[] { "Daft Punk" }, track.Artists);
        Assert.Equal(224, track.DurationSeconds);
        Assert.Equal("GBDUW0000059", track.Isrc);
    }

    [Fact]
    public void Deezer_IgnoresErrorPayload() => Assert.Null(DeezerClient.ParseTrack("{\"error\":{\"type\":\"DataException\"}}"));

    [Theory]
    [InlineData("gb-duw-00-00059", "GBDUW0000059")]
    [InlineData("gbduw0000059", "GBDUW0000059")]
    [InlineData("nope", null)]
    [InlineData(null, null)]
    public void IsrcsAreNormalisedBeforeTheyAreUsedAsIdentity(string? raw, string? expected) =>
        Assert.Equal(expected, SpotifyClient.NormalizeIsrc(raw));

    [Fact]
    public void SearchResultPlausibility()
    {
        var track = new SpotifyTrack("1", "Creep", new() { new MediaArtist("Radiohead", "r1") }, 238, null);
        Assert.True(MediaResolver.IsPlausible(track, "Radiohead - Creep (Official Video)", "Creep", new() { "Radiohead" }));
        Assert.False(MediaResolver.IsPlausible(track, "Some Other Song", "Some Other Song", new() { "Someone" }));
        Assert.False(MediaResolver.IsPlausible(track, "Creep (cover)", "Creep", new() { "Random Guy" }));
    }

    [Fact]
    public void Score_RejectsResultsWhoseLengthIsNothingLikeThePostedMedia()
    {
        var track = new SpotifyTrack("1", "Creep", new() { new MediaArtist("Radiohead", "r1") }, 238, null);
        var hints = new List<string> { "Radiohead" };
        Assert.True(MediaResolver.Score(track, "Radiohead - Creep (Live at Glastonbury)", "Creep", hints, 600) < 0);
        Assert.True(MediaResolver.Score(track, "Radiohead - Creep", "Creep", hints, 240) > 0.9);
    }

    [Fact]
    public void Score_PrefersTheResultWithTheCloserDuration()
    {
        var hints = new List<string> { "Radiohead" };
        var studio = new SpotifyTrack("1", "Creep", new() { new MediaArtist("Radiohead", "r1") }, 238, null);
        var extended = new SpotifyTrack("2", "Creep", new() { new MediaArtist("Radiohead", "r1") }, 300, null);
        Assert.True(MediaResolver.Score(studio, "Radiohead - Creep", "Creep", hints, 238) >
                    MediaResolver.Score(extended, "Radiohead - Creep", "Creep", hints, 238));
    }

    [Theory]
    [InlineData("https://open.spotify.com/track/abc?si=123&utm_source=copy-link", "https://open.spotify.com/track/abc")]
    [InlineData("https://www.youtube.com/watch?v=abc&t=42s&feature=share", "https://www.youtube.com/watch?v=abc")]
    public void CacheKey_IgnoresTrackingParametersSoOneLinkResolvesOnce(string url, string equivalent) =>
        Assert.Equal(MediaResolver.CacheKey(equivalent), MediaResolver.CacheKey(url));
}

/// <summary>Runs CatalogWriter against a real (temporary) sqlite file, with Spotify faked.</summary>
public class CatalogWriterTests : IDisposable
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sotd-test-{Guid.NewGuid():N}.db");
    readonly SotdDbContext _db;
    readonly SpotifyClient _spotify;
    readonly ReviewLog _log = new(Path.Combine(Path.GetTempPath(), $"sotd-logs-{Guid.NewGuid():N}"));
    readonly Message _message;

    class FakeSpotify : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            var body = url.Contains("/api/token") ? "{\"access_token\":\"t\",\"expires_in\":3600}"
                : url.Contains("/v1/artists/") ? "{\"genres\":[\"art rock\",\"alternative rock\"]}"
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    public CatalogWriterTests()
    {
        _db = SotdDbContext.ForSqliteFile(_dbPath);
        _db.Database.Migrate();
        _spotify = new SpotifyClient(new HttpClient(new FakeSpotify()), "id", "secret");

        var channel = new Channel { Id = Guid.NewGuid(), DiscordId = 1, Name = "sotd" };
        var profile = new Profile { Id = Guid.NewGuid(), DiscordId = 2, Username = "u" };
        _message = new Message { Id = Guid.NewGuid(), DiscordId = 3, ChannelId = channel.Id, PostedById = profile.Id, PostedAt = DateTime.UtcNow };
        _db.AddRange(channel, profile, _message);
        _db.SaveChanges();
    }

    CatalogWriter Writer() => new(_db, SongTitleIndex.LoadAsync(_db).Result, _spotify, _log);

    static ResolvedMedia Media(string title, string source, string? spotifyId, params MediaArtist[] artists) => new()
    {
        Title = title, SourceName = source, Link = "https://x/" + Guid.NewGuid(), Artists = artists.ToList(),
        SpotifyTrackId = spotifyId, DurationSeconds = 200,
        Quality = spotifyId != null ? MatchQuality.SpotifySearch : MatchQuality.Raw,
    };

    [Fact]
    public async Task SameRecordingFromTwoServices_IsOneSongWhenTheIsrcMatches()
    {
        var daft = new MediaArtist("Daft Punk", "a1");
        var w = Writer();
        var fromDeezer = Media("Harder Better Faster Stronger", "deezer", "t1", daft);
        fromDeezer.Isrc = "GBDUW0000059";
        var fromApple = Media("Harder, Better, Faster, Stronger", "apple music", "t2", daft);
        fromApple.Isrc = "GBDUW0000059";
        await w.AddEntryAsync(_message, fromDeezer, null, "ctx");
        await w.AddEntryAsync(_message, fromApple, null, "ctx");

        Assert.Equal(1, await _db.Songs.CountAsync());
        Assert.Equal(2, await _db.SotdEntries.CountAsync());
    }

    [Fact]
    public async Task ReissueOfTheSameTitle_IsNotASecondSong()
    {
        var radiohead = new MediaArtist("Radiohead", "r1");
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Creep", "youtube", null, radiohead), null, "ctx");
        await w.AddEntryAsync(_message, Media("Creep - Remastered 2008", "apple music", null, radiohead), null, "ctx");

        Assert.Equal(1, await _db.Songs.CountAsync());
    }

    [Fact]
    public async Task SameSpotifyTrackFromTwoSources_IsOneSong()
    {
        var radiohead = new MediaArtist("Radiohead", "r1");
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Creep", "youtube", "t1", radiohead), null, "ctx");
        await w.AddEntryAsync(_message, Media("Creep", "spotify", "t1", radiohead), null, "ctx");

        Assert.Equal(1, await _db.Songs.CountAsync());
        Assert.Equal(1, await _db.Artists.CountAsync());
        Assert.Equal(2, await _db.SotdEntries.CountAsync());
        Assert.Equal(2, await _db.Sources.CountAsync());
        Assert.Equal(2, await _db.ArtistGenres.CountAsync());
        Assert.Equal(0, await _db.SongGenres.CountAsync());
    }

    [Fact]
    public async Task NameOnlySongThenSpotifySong_AdoptsSpotifyIds()
    {
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Creep", "youtube", null, new MediaArtist("Radiohead", null)), null, "ctx");
        await w.AddEntryAsync(_message, Media("creep", "spotify", "t1", new MediaArtist("Radiohead", "r1")), null, "ctx");

        var song = await _db.Songs.SingleAsync();
        Assert.Equal("t1", song.SpotifyId);
        Assert.Equal("r1", (await _db.Artists.SingleAsync()).SpotifyId);
    }

    [Fact]
    public async Task SameTitleDifferentArtists_AreDifferentSongs()
    {
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Home", "youtube", null, new MediaArtist("Artist A", null)), null, "ctx");
        await w.AddEntryAsync(_message, Media("Home", "youtube", null, new MediaArtist("Artist B", null)), null, "ctx");
        Assert.Equal(2, await _db.Songs.CountAsync());
    }

    [Fact]
    public async Task ArtistPositionsFollowCreditOrder()
    {
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Collab", "spotify", "t2", new MediaArtist("A", "a1"), new MediaArtist("B", "b1")), null, "ctx");
        var names = await _db.SongArtists.Include(x => x.Artist).OrderBy(x => x.Position).Select(x => x.Artist.Name).ToListAsync();
        Assert.Equal(new[] { "A", "B" }, names);
    }

    [Fact]
    public void DefaultEmojiHaveUniqueNamesAndNoDiscordId()
    {
        _db.Emojis.Add(new Emoji { Id = Guid.NewGuid(), Name = "👍" });
        _db.SaveChanges();
        _db.Emojis.Add(new Emoji { Id = Guid.NewGuid(), Name = "👍" });
        Assert.Throws<DbUpdateException>(() => _db.SaveChanges());
    }

    [Fact]
    public void DeletingReferencedRowFails()
    {
        // No cascade deletes: removing a profile that has messages must fail.
        Assert.ThrowsAny<Exception>(() => _db.Database.ExecuteSqlRaw("DELETE FROM Profiles"));
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*")) File.Delete(f);
    }
}
