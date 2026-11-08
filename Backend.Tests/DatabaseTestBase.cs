using Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests;

/// <summary>
/// A throwaway sqlite file with the real migrations applied. The services are mostly EF queries and the
/// error handling exists to catch the database's own constraints, so a fake provider would test nothing.
/// </summary>
public abstract class DatabaseTestBase
{
    string _dbPath = "";
    protected SotdDbContext Db { get; private set; } = null!;

    [SetUp]
    public void CreateDatabase()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sotd-backend-test-{Guid.NewGuid():N}.db");
        Db = SotdDbContext.ForSqliteFile(_dbPath);
        Db.Database.Migrate();
    }

    [TearDown]
    public void DropDatabase()
    {
        Db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*")) File.Delete(f);
    }

    protected Person AddPerson(string name)
    {
        var person = new Person { Id = Guid.NewGuid(), Name = name };
        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    protected Artist AddArtist(string name)
    {
        var artist = new Artist { Id = Guid.NewGuid(), Name = name };
        Db.Artists.Add(artist);
        Db.SaveChanges();
        return artist;
    }

    protected Genre AddGenre(string name, Guid? parentId = null)
    {
        var genre = new Genre { Id = Guid.NewGuid(), Name = name, ParentId = parentId };
        Db.Genres.Add(genre);
        Db.SaveChanges();
        return genre;
    }

    protected Song AddSong(string title, string? spotifyId = null)
    {
        var song = new Song { Id = Guid.NewGuid(), Title = title, SpotifyId = spotifyId };
        Db.Songs.Add(song);
        Db.SaveChanges();
        return song;
    }

    protected Profile AddProfile(string username, long discordId)
    {
        var profile = new Profile { Id = Guid.NewGuid(), Username = username, DiscordId = discordId };
        Db.Profiles.Add(profile);
        Db.SaveChanges();
        return profile;
    }
}
