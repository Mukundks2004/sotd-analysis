using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sotd.Database;

namespace Importer.Tests;

/// <summary>
/// Runs the writer against a real (temporary) sqlite file with Spotify mocked, because the matching rules
/// it implements are mostly about what the database already contains.
/// </summary>
[TestFixture]
public class CatalogWriterTests : DatabaseTestBase
{
    Mock<ISpotifyClient> _spotify = null!;
    Mock<IReviewLog> _log = null!;
    Message _message = null!;

    [SetUp]
    public void SetUpWriter()
    {
        _spotify = new Mock<ISpotifyClient>();
        _spotify.Setup(s => s.GetArtistGenresAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "art rock", "alternative rock" });
        _log = new Mock<IReviewLog>();
        _message = SeedMessage();
    }

    ICatalogWriter Writer()
    {
        var index = new SongTitleIndex();
        index.LoadAsync(Db).GetAwaiter().GetResult();
        return new CatalogWriterFactory(index, _spotify.Object, _log.Object).Create(Db);
    }

    static ResolvedMedia Media(string title, string source, string? spotifyId, params MediaArtist[] artists) => new()
    {
        Title = title,
        SourceName = source,
        Link = "https://x/" + Guid.NewGuid(),
        Artists = artists.ToList(),
        SpotifyTrackId = spotifyId,
        DurationSeconds = 200,
        Quality = spotifyId != null ? MatchQuality.SpotifySearch : MatchQuality.Raw,
    };

    [Test]
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

        Assert.That(await Db.Songs.CountAsync(), Is.EqualTo(1));
        Assert.That(await Db.SotdEntries.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task ReissueOfTheSameTitle_IsNotASecondSong()
    {
        var radiohead = new MediaArtist("Radiohead", "r1");
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Creep", "youtube", null, radiohead), null, "ctx");
        await w.AddEntryAsync(_message, Media("Creep - Remastered 2008", "apple music", null, radiohead), null, "ctx");

        Assert.That(await Db.Songs.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task SameSpotifyTrackFromTwoSources_IsOneSong()
    {
        var radiohead = new MediaArtist("Radiohead", "r1");
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Creep", "youtube", "t1", radiohead), null, "ctx");
        await w.AddEntryAsync(_message, Media("Creep", "spotify", "t1", radiohead), null, "ctx");

        Assert.That(await Db.Songs.CountAsync(), Is.EqualTo(1));
        Assert.That(await Db.Artists.CountAsync(), Is.EqualTo(1));
        Assert.That(await Db.SotdEntries.CountAsync(), Is.EqualTo(2));
        Assert.That(await Db.LinkSources.CountAsync(), Is.EqualTo(2));
        // Genres hang off the artist, not the song, and are only asked for once.
        Assert.That(await Db.ArtistGenres.CountAsync(), Is.EqualTo(2));
        Assert.That(await Db.SongGenres.CountAsync(), Is.EqualTo(0));
        _spotify.Verify(s => s.GetArtistGenresAsync("r1"), Times.Once);
    }

    [Test]
    public async Task NameOnlySongThenSpotifySong_AdoptsSpotifyIds()
    {
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Creep", "youtube", null, new MediaArtist("Radiohead", null)), null, "ctx");
        await w.AddEntryAsync(_message, Media("creep", "spotify", "t1", new MediaArtist("Radiohead", "r1")), null, "ctx");

        Assert.That((await Db.Songs.SingleAsync()).SpotifyId, Is.EqualTo("t1"));
        Assert.That((await Db.Artists.SingleAsync()).SpotifyId, Is.EqualTo("r1"));
    }

    [Test]
    public async Task SameTitleDifferentArtists_AreDifferentSongs()
    {
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Home", "youtube", null, new MediaArtist("Artist A", null)), null, "ctx");
        await w.AddEntryAsync(_message, Media("Home", "youtube", null, new MediaArtist("Artist B", null)), null, "ctx");

        Assert.That(await Db.Songs.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task ArtistPositionsFollowCreditOrder()
    {
        var w = Writer();
        await w.AddEntryAsync(_message,
            Media("Collab", "spotify", "t2", new MediaArtist("A", "a1"), new MediaArtist("B", "b1")), null, "ctx");

        var names = await Db.SongArtists.Include(x => x.Artist).OrderBy(x => x.Position)
            .Select(x => x.Artist.Name).ToListAsync();
        Assert.That(names, Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public async Task NameMatchedSongsGoInTheReviewLog()
    {
        var w = Writer();
        await w.AddEntryAsync(_message, Media("Creep", "youtube", null, new MediaArtist("Radiohead", null)), null, "ctx");

        _log.Verify(l => l.NameMatched("ctx", It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task ASecondRunReusesWhatTheFirstRunWroteInsteadOfDuplicatingIt()
    {
        // Writer() builds a fresh SongTitleIndex from the database, which is exactly what starting the
        // importer again does. If the de-duplication only lived in memory, this would write everything twice.
        var daft = new MediaArtist("Daft Punk", "a1");
        await Writer().AddEntryAsync(_message, Media("One More Time", "spotify", "t1", daft), null, "ctx");

        await Writer().AddEntryAsync(_message, Media("One More Time", "spotify", "t1", daft), null, "ctx");

        Assert.Multiple(async () =>
        {
            Assert.That(await Db.Songs.CountAsync(), Is.EqualTo(1), "the song should be matched, not re-created");
            Assert.That(await Db.Artists.CountAsync(), Is.EqualTo(1), "the artist should be matched, not re-created");
            Assert.That(await Db.Genres.CountAsync(), Is.EqualTo(2), "genres should not accumulate per run");
        });
    }
}
