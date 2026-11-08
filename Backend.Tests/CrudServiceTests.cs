using System.Linq.Expressions;
using Backend.Api;
using Backend.Dtos;
using Backend.Services;
using Database;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Tests;

/// <summary>
/// The behaviour every table's endpoints share. A real table definition is used for the happy paths and a
/// mocked one where the point is that the service asks the definition rather than knowing the table.
/// </summary>
[TestFixture]
public class CrudServiceTests : DatabaseTestBase
{
    IQueryBuilder _query = null!;

    [SetUp]
    public void SetUpService() => _query = new QueryBuilder();

    CrudService<Person, PersonDto, PersonWrite> People() =>
        new(Db, new PersonTable(), _query);

    CrudService<Song, SongDto, SongWrite> Songs() =>
        new(Db, new SongTable(), _query);

    [Test]
    public async Task ASmallTableReturnsEveryRowInOnePage()
    {
        for (var i = 0; i < 30; i++) AddPerson($"person {i:00}");

        var result = await People().ListAsync(new ListQuery { PageSize = 10 });

        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Value!.Items, Has.Count.EqualTo(30));
        Assert.That(result.Value.TotalPages, Is.EqualTo(1));
    }

    [Test]
    public async Task ABigTablePagesAndReportsTheUnpagedTotal()
    {
        for (var i = 0; i < 25; i++) AddSong($"song {i:00}");

        var result = await Songs().ListAsync(new ListQuery { Page = 2, PageSize = 10 });

        Assert.That(result.Value!.Items, Has.Count.EqualTo(10));
        Assert.That(result.Value.Total, Is.EqualTo(25));
        Assert.That(result.Value.Page, Is.EqualTo(2));
        Assert.That(result.Value.TotalPages, Is.EqualTo(3));
        Assert.That(result.Value.Items[0].Title, Is.EqualTo("song 10"));
    }

    [Test]
    public async Task TheTotalCountsTheFilteredRowsNotTheWholeTable()
    {
        AddSong("Creep");
        AddSong("Creep (live)");
        AddSong("Karma Police");

        var result = await Songs().ListAsync(new ListQuery { Filter = "creep" });

        Assert.That(result.Value!.Total, Is.EqualTo(2));
    }

    [Test]
    public async Task GettingAMissingRowIsNotFoundRatherThanAnError()
    {
        var result = await People().GetAsync(Guid.NewGuid());

        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
        Assert.That(result.Message, Does.Contain("No row"));
    }

    [Test]
    public async Task CreatingReturnsTheStoredRow()
    {
        var result = await People().CreateAsync(new PersonWrite("  Alice  "));

        Assert.That(result.IsOk, Is.True);
        // The write is trimmed by the table definition before it is stored.
        Assert.That(result.Value!.Name, Is.EqualTo("Alice"));
        Assert.That(await Db.People.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task UpdatingAMissingRowChangesNothing()
    {
        var result = await People().UpdateAsync(Guid.NewGuid(), new PersonWrite("Alice"));

        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
        Assert.That(await Db.People.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task UpdatingKeepsTheIdAndReplacesTheValues()
    {
        var person = AddPerson("Alice");
        Db.ChangeTracker.Clear();

        var result = await People().UpdateAsync(person.Id, new PersonWrite("Alicia"));

        Assert.That(result.Value!.Id, Is.EqualTo(person.Id));
        Assert.That(result.Value.Name, Is.EqualTo("Alicia"));
    }

    [Test]
    public async Task DeletingNothingIsARequestProblem()
    {
        var result = await People().DeleteAsync(Array.Empty<Guid>());

        Assert.That(result.Status, Is.EqualTo(ResultStatus.BadRequest));
    }

    [Test]
    public async Task DeletingIsAllOrNothing()
    {
        var alice = AddPerson("Alice");
        var bob = AddPerson("Bob");
        Db.ChangeTracker.Clear();

        // One id in the selection is already gone, so none of them may be removed.
        var result = await People().DeleteAsync(new[] { alice.Id, Guid.NewGuid(), bob.Id });

        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
        Assert.That(result.Message, Does.Contain("Nothing was deleted"));
        Assert.That(await Db.People.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task DeletingASelectionRemovesAllOfIt()
    {
        var alice = AddPerson("Alice");
        var bob = AddPerson("Bob");
        AddPerson("Carol");
        Db.ChangeTracker.Clear();

        var result = await People().DeleteAsync(new[] { alice.Id, bob.Id });

        Assert.That(result.IsOk, Is.True);
        Assert.That(await Db.People.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task ARowSomethingElsePointsAtCannotBeDeleted()
    {
        var song = AddSong("Creep");
        var artist = AddArtist("Radiohead");
        Db.SongArtists.Add(new SongArtist { SongId = song.Id, ArtistId = artist.Id });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        var result = await Songs().DeleteAsync(new[] { song.Id });

        // SQLite refuses it and the service turns that into something the toast can show.
        Assert.That(result.Status, Is.EqualTo(ResultStatus.Conflict));
        Assert.That(result.Message, Does.Contain("foreign key"));
        Assert.That(await Db.Songs.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task AFailedSaveLeavesNothingPendingForTheNextRequest()
    {
        var song = AddSong("Creep");
        var artist = AddArtist("Radiohead");
        Db.SongArtists.Add(new SongArtist { SongId = song.Id, ArtistId = artist.Id });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        var service = Songs();
        await service.DeleteAsync(new[] { song.Id });
        // The change tracker is cleared on failure, so the next call is not dragged down by the old delete.
        var after = await service.CreateAsync(new SongWrite("Karma Police", null, null));

        Assert.That(after.IsOk, Is.True);
        Assert.That(await Db.Songs.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task TheServiceAsksTheDefinitionForEverythingTableSpecific()
    {
        AddPerson("Alice");
        var table = new Mock<ITableDefinition<Person, PersonDto, PersonWrite>>();
        table.Setup(t => t.Set(It.IsAny<SotdDbContext>())).Returns((SotdDbContext db) => db.People);
        table.Setup(t => t.Include(It.IsAny<IQueryable<Person>>())).Returns((IQueryable<Person> q) => q);
        table.SetupGet(t => t.Columns).Returns(new[] { Query.Text<Person>("name", p => p.Name) });
        table.SetupGet(t => t.IdSelector).Returns(p => p.Id);
        table.SetupGet(t => t.DefaultColumn).Returns("name");
        table.SetupGet(t => t.DefaultDesc).Returns(false);
        table.SetupGet(t => t.IsBig).Returns(false);
        table.Setup(t => t.ToRead(It.IsAny<Person>())).Returns((Person p) => new PersonDto(p.Id, p.Name.ToUpperInvariant()));

        var result = await new CrudService<Person, PersonDto, PersonWrite>(Db, table.Object, _query).ListAsync(new ListQuery());

        Assert.That(result.Value!.Items.Single().Name, Is.EqualTo("ALICE"));
        table.Verify(t => t.ToRead(It.IsAny<Person>()), Times.Once);
        table.Verify(t => t.Include(It.IsAny<IQueryable<Person>>()), Times.Once);
    }

    [Test]
    public async Task LookupsReadTheIdSelectorOnlyOnceSoEfCanTranslateThem()
    {
        var person = AddPerson("Alice");
        Db.ChangeTracker.Clear();

        // A definition that hands back a fresh expression every time is the realistic case: reading it
        // twice would produce a predicate with two different parameters, which EF cannot translate.
        var reads = 0;
        var table = new Mock<ITableDefinition<Person, PersonDto, PersonWrite>>();
        table.Setup(t => t.Set(It.IsAny<SotdDbContext>())).Returns((SotdDbContext db) => db.People);
        table.Setup(t => t.Include(It.IsAny<IQueryable<Person>>())).Returns((IQueryable<Person> q) => q);
        table.SetupGet(t => t.Columns).Returns(Array.Empty<ColumnMap<Person>>());
        table.SetupGet(t => t.IdSelector).Returns(() =>
        {
            reads++;
            var parameter = Expression.Parameter(typeof(Person), "p");
            return Expression.Lambda<Func<Person, Guid>>(Expression.Property(parameter, nameof(Person.Id)), parameter);
        });
        table.SetupGet(t => t.DefaultColumn).Returns("name");
        table.SetupGet(t => t.IsBig).Returns(false);
        table.Setup(t => t.ToRead(It.IsAny<Person>())).Returns((Person p) => new PersonDto(p.Id, p.Name));

        var result = await new CrudService<Person, PersonDto, PersonWrite>(Db, table.Object, _query).GetAsync(person.Id);

        Assert.That(result.IsOk, Is.True);
        Assert.That(reads, Is.EqualTo(1));
    }
}
