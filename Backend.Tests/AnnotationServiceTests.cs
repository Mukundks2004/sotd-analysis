using Microsoft.EntityFrameworkCore;
using Sotd.Backend.Api;
using Sotd.Backend.Services;
using Sotd.Database;

namespace Backend.Tests;

/// <summary>The song-to-artist relationship, which is the only ordered one.</summary>
[TestFixture]
public class SongArtistAnnotationTests : AnnotationTestBase
{
    [Test]
    public async Task ANewCreditGoesOnTheEndOfTheRunningOrder()
    {
        var song = AddSong("Under Pressure");
        await Service.AddTagAsync(AnnotationService.SongArtists, Tag(song.Id, name: "Queen"));
        await Service.AddTagAsync(AnnotationService.SongArtists, Tag(song.Id, name: "David Bowie"));

        var positions = await Db.SongArtists.AsNoTracking().Where(sa => sa.SongId == song.Id)
            .OrderBy(sa => sa.Position).Select(sa => new { sa.Position, sa.Artist.Name }).ToListAsync();

        Assert.That(positions.Select(p => p.Name), Is.EqualTo(new[] { "Queen", "David Bowie" }));
        Assert.That(positions.Select(p => p.Position), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task TypingTheNameOfAnExistingArtistReusesItRatherThanMakingASecond()
    {
        var existing = AddArtist("Radiohead");
        var song = AddSong("Creep");

        var result = await Service.AddTagAsync(AnnotationService.SongArtists,
            Tag(song.Id, name: "  Radiohead  "));

        Assert.That(result.Value!.Id, Is.EqualTo(existing.Id));
        Assert.That(await Db.Artists.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task TheSameArtistCannotBeCreditedTwice()
    {
        var song = AddSong("Creep");
        var artist = AddArtist("Radiohead");
        await Service.AddTagAsync(AnnotationService.SongArtists, Tag(song.Id, id: artist.Id));

        var again = await Service.AddTagAsync(AnnotationService.SongArtists, Tag(song.Id, id: artist.Id));

        Assert.That(again.Status, Is.EqualTo(ResultStatus.Conflict));
        Assert.That(again.Message, Does.Contain("already credited"));
    }

    [Test]
    public async Task RemovingACreditClosesTheGapInThePositions()
    {
        var song = AddSong("Under Pressure");
        foreach (var name in new[] { "Queen", "David Bowie", "Someone Else" })
            await Service.AddTagAsync(AnnotationService.SongArtists, Tag(song.Id, name: name));
        var middle = await Db.Artists.FirstAsync(a => a.Name == "David Bowie");

        await Service.RemoveTagAsync(AnnotationService.SongArtists, song.Id, middle.Id);

        var positions = await Db.SongArtists.AsNoTracking().Where(sa => sa.SongId == song.Id)
            .OrderBy(sa => sa.Position).Select(sa => sa.Position).ToListAsync();
        Assert.That(positions, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task ReorderingRenumbersTheCredits()
    {
        var song = AddSong("Under Pressure");
        foreach (var name in new[] { "Queen", "David Bowie" })
            await Service.AddTagAsync(AnnotationService.SongArtists, Tag(song.Id, name: name));
        var bowie = await Db.Artists.FirstAsync(a => a.Name == "David Bowie");
        var queen = await Db.Artists.FirstAsync(a => a.Name == "Queen");

        var result = await Service.ReorderAsync(Reorder(song.Id, bowie.Id, queen.Id));

        Assert.That(result.Value!.Select(t => t.Name), Is.EqualTo(new[] { "David Bowie", "Queen" }));
    }

    [Test]
    public async Task AReorderThatDoesNotListEveryArtistIsRejected()
    {
        var song = AddSong("Under Pressure");
        foreach (var name in new[] { "Queen", "David Bowie" })
            await Service.AddTagAsync(AnnotationService.SongArtists, Tag(song.Id, name: name));
        var queen = await Db.Artists.FirstAsync(a => a.Name == "Queen");

        var missing = await Service.ReorderAsync(Reorder(song.Id, queen.Id));
        var foreign = await Service.ReorderAsync(Reorder(song.Id, queen.Id, Guid.NewGuid()));

        Assert.That(missing.Status, Is.EqualTo(ResultStatus.BadRequest));
        Assert.That(foreign.Status, Is.EqualTo(ResultStatus.BadRequest));
    }

    [Test]
    public async Task ListingShowsEachSongWithItsCreditsInOrder()
    {
        var song = AddSong("Under Pressure", "sp1");
        foreach (var name in new[] { "Queen", "David Bowie" })
            await Service.AddTagAsync(AnnotationService.SongArtists, Tag(song.Id, name: name));
        AddSong("Creep");

        var result = await Service.ListAsync(AnnotationService.SongArtists, new ListQuery());

        Assert.That(result.Value!.Items, Has.Count.EqualTo(2));
        var row = result.Value.Items.Single(r => r.Id == song.Id);
        Assert.That(row.Detail, Is.EqualTo("sp1"));
        Assert.That(row.Tags.Select(t => t.Name), Is.EqualTo(new[] { "Queen", "David Bowie" }));
        Assert.That(result.Value.Items.Single(r => r.Label == "Creep").Tags, Is.Empty);
    }
}

/// <summary>The unordered relationships, and the rules shared by all three.</summary>
[TestFixture]
public class AnnotationServiceTests : AnnotationTestBase
{
    [Test]
    public void OnlyTheThreeEditableRelationshipsAreOffered()
    {
        var names = Service.Relationships().Select(r => r.Key).ToList();

        Assert.That(names, Is.EquivalentTo(new[] { "song-artists", "song-genres", "profile-persons" }));
        Assert.That(Service.Relationships().Single(r => r.Key == "song-artists").Ordered, Is.True);
        Assert.That(Service.Relationships().Single(r => r.Key == "song-genres").Ordered, Is.False);
    }

    [TestCase("songs")]
    [TestCase("")]
    public async Task AnUnknownRelationshipIsNotFoundEverywhere(string relationship)
    {
        Assert.That((await Service.ListAsync(relationship, new ListQuery())).Status, Is.EqualTo(ResultStatus.NotFound));
        Assert.That((await Service.OptionsAsync(relationship, null, 20)).Status, Is.EqualTo(ResultStatus.NotFound));
        Assert.That((await Service.AddTagAsync(relationship, Tag(Guid.NewGuid(), name: "x"))).Status,
            Is.EqualTo(ResultStatus.NotFound));
        Assert.That((await Service.RemoveTagAsync(relationship, Guid.NewGuid(), Guid.NewGuid())).Status,
            Is.EqualTo(ResultStatus.NotFound));
    }

    [Test]
    public async Task TaggingARowThatIsGoneIsNotFound()
    {
        var result = await Service.AddTagAsync(AnnotationService.SongGenres,
            Tag(Guid.NewGuid(), name: "Rock"));

        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
    }

    [Test]
    public async Task ATagNeedsEitherAPickOrAName()
    {
        var song = AddSong("Creep");

        var empty = await Service.AddTagAsync(AnnotationService.SongGenres, Tag(song.Id, name: null));
        var blank = await Service.AddTagAsync(AnnotationService.SongGenres, Tag(song.Id, name: "   "));

        Assert.That(empty.Status, Is.EqualTo(ResultStatus.BadRequest));
        Assert.That(blank.Status, Is.EqualTo(ResultStatus.BadRequest));
    }

    [Test]
    public async Task PickingATagThatHasSinceBeenDeletedIsNotFound()
    {
        var song = AddSong("Creep");

        var result = await Service.AddTagAsync(AnnotationService.SongGenres,
            Tag(song.Id, id: Guid.NewGuid()));

        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
    }

    [Test]
    public async Task AGenreCannotBeAddedToASongTwice()
    {
        var song = AddSong("Creep");
        await Service.AddTagAsync(AnnotationService.SongGenres, Tag(song.Id, name: "Rock"));

        var again = await Service.AddTagAsync(AnnotationService.SongGenres, Tag(song.Id, name: "Rock"));

        Assert.That(again.Status, Is.EqualTo(ResultStatus.Conflict));
    }

    [Test]
    public async Task RemovingATagThatIsAlreadyGoneIsNotFound()
    {
        var song = AddSong("Creep");

        var result = await Service.RemoveTagAsync(AnnotationService.SongGenres, song.Id, Guid.NewGuid());

        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
    }

    [Test]
    public async Task ProfilesAreASmallTableSoEveryRowIsListedAtOnce()
    {
        for (var i = 0; i < 30; i++) AddProfile($"user{i:00}", 100 + i);
        var profile = await Db.Profiles.FirstAsync(p => p.Username == "user00");
        await Service.AddTagAsync(AnnotationService.ProfilePersons, Tag(profile.Id, name: "Alice"));

        var result = await Service.ListAsync(AnnotationService.ProfilePersons, new ListQuery { PageSize = 10 });

        Assert.That(result.Value!.Items, Has.Count.EqualTo(30));
        Assert.That(result.Value.TotalPages, Is.EqualTo(1));
        Assert.That(result.Value.Items.Single(r => r.Id == profile.Id).Tags.Single().Name, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task OptionsWithNoTermShowEverythingSoTheDropdownIsUsefulWhenFirstClicked()
    {
        AddArtist("Radiohead");
        AddArtist("Daft Punk");

        var result = await Service.OptionsAsync(AnnotationService.SongArtists, null, 20);

        Assert.That(result.Value!.Select(t => t.Name), Is.EqualTo(new[] { "Daft Punk", "Radiohead" }));
    }

    [Test]
    public async Task OptionsAreNarrowedByTheTypedTerm()
    {
        AddArtist("Radiohead");
        AddArtist("Daft Punk");

        var result = await Service.OptionsAsync(AnnotationService.SongArtists, " head ", 20);

        Assert.That(result.Value!.Select(t => t.Name), Is.EqualTo(new[] { "Radiohead" }));
    }

    [TestCase(0, 1)]
    [TestCase(-1, 1)]
    [TestCase(5000, 3)]
    public async Task TheOptionLimitIsClampedToSomethingSendable(int limit, int expected)
    {
        AddArtist("A");
        AddArtist("B");
        AddArtist("C");

        var result = await Service.OptionsAsync(AnnotationService.SongArtists, null, limit);

        Assert.That(result.Value!, Has.Count.EqualTo(expected));
    }
}

public abstract class AnnotationTestBase : DatabaseTestBase
{
    protected AnnotationService Service { get; private set; } = null!;

    [SetUp]
    public void SetUpService() => Service = new AnnotationService(Db, new QueryBuilder());

    protected static AddTagRequest Tag(Guid ownerId, Guid? id = null, string? name = null) =>
        new() { OwnerId = ownerId, TagId = id, NewName = name };

    protected static ReorderRequest Reorder(Guid songId, params Guid[] artistIds) =>
        new() { SongId = songId, ArtistIds = artistIds };
}
