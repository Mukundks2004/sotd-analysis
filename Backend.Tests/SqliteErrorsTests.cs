using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sotd.Backend.Services;
using Sotd.Database;

namespace Backend.Tests;

/// <summary>
/// SQLite's constraint text is the only explanation the UI ever gets for a refused write, so it is
/// translated into something a person can act on.
/// </summary>
[TestFixture]
public class SqliteErrorsTests : DatabaseTestBase
{
    /// <summary>Provokes a real failure the same way the services meet it, because the wording is the thing under test.</summary>
    SqliteException Provoke(Action change)
    {
        change();
        try
        {
            Db.SaveChanges();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlite)
        {
            Db.ChangeTracker.Clear();
            return sqlite;
        }
        throw new InvalidOperationException("That change was expected to fail.");
    }

    [Test]
    public void AForeignKeyFailureExplainsBothDirections()
    {
        var song = AddSong("Creep");
        var artist = AddArtist("Radiohead");
        Db.SongArtists.Add(new SongArtist { SongId = song.Id, ArtistId = artist.Id });
        Db.SaveChanges();
        // Forget the credit, so EF sends the delete and SQLite is the one that refuses it.
        Db.ChangeTracker.Clear();

        var message = SqliteErrors.Describe(Provoke(() =>
            Db.Songs.Remove(Db.Songs.First(s => s.Id == song.Id))));

        Assert.That(message, Does.Contain("foreign key"));
        Assert.That(message, Does.Contain("still refers to this row"));
    }

    [Test]
    public void AUniqueFailureNamesTheColumnThatIsTaken()
    {
        AddGenre("Rock");

        var message = SqliteErrors.Describe(Provoke(() =>
            Db.Genres.Add(new Genre { Id = Guid.NewGuid(), Name = "Rock" })));

        Assert.That(message, Does.Contain("already taken"));
        Assert.That(message, Does.Contain("Genres.Name"));
    }

    [Test]
    public void ANotNullFailureNamesTheMissingColumn()
    {
        var message = SqliteErrors.Describe(Provoke(() =>
            Db.Genres.Add(new Genre { Id = Guid.NewGuid(), Name = null! })));

        Assert.That(message, Does.Contain("required value is missing"));
        Assert.That(message, Does.Contain("Genres.Name"));
    }

    [Test]
    public void AnythingElseIsPassedThroughUnchanged()
    {
        var ex = new SqliteException("database is locked", 5);

        Assert.That(SqliteErrors.Describe(ex), Is.EqualTo(ex.Message));
        Assert.That(SqliteErrors.DescribeLink(ex), Is.EqualTo(ex.Message));
    }

    [Test]
    public void TheAnnotationScreenTalksAboutRowsRatherThanColumns()
    {
        var song = AddSong("Creep");

        var foreignKey = SqliteErrors.DescribeLink(Provoke(() =>
            Db.SongGenres.Add(new SongGenre { SongId = song.Id, GenreId = Guid.NewGuid() })));
        AddGenre("Rock");
        var unique = SqliteErrors.DescribeLink(Provoke(() =>
            Db.Genres.Add(new Genre { Id = Guid.NewGuid(), Name = "Rock" })));

        Assert.That(foreignKey, Does.Contain("no longer exists"));
        Assert.That(unique, Is.EqualTo("That name is already taken."));
    }
}
