using Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Importer.Tests;

/// <summary>
/// A throwaway sqlite file with the real migrations applied. The schema is the thing under test in a lot
/// of these cases (unique names, no cascade deletes), so an in-memory provider would not do.
/// </summary>
public abstract class DatabaseTestBase
{
    protected string DbPath { get; private set; } = "";
    protected SotdDbContext Db { get; private set; } = null!;

    [SetUp]
    public void CreateDatabase()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"sotd-test-{Guid.NewGuid():N}.db");
        Db = SotdDbContext.ForSqliteFile(DbPath);
        Db.Database.Migrate();
    }

    [TearDown]
    public void DropDatabase()
    {
        Db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(DbPath) + "*")) File.Delete(f);
    }

    protected Message SeedMessage()
    {
        var channel = new Channel { Id = Guid.NewGuid(), DiscordId = 1, Name = "sotd" };
        var profile = new Profile { Id = Guid.NewGuid(), DiscordId = 2, Username = "u" };
        var message = new Message
        {
            Id = Guid.NewGuid(), DiscordId = 3, ChannelId = channel.Id, PostedById = profile.Id,
            PostedAt = DateTime.UtcNow,
        };
        Db.AddRange(channel, profile, message);
        Db.SaveChanges();
        return message;
    }
}
