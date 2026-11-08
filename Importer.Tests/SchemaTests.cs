using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Sotd.Database;

namespace Importer.Tests;

/// <summary>
/// The rules the database itself enforces, which the importer relies on rather than re-checking in code.
/// </summary>
[TestFixture]
public class SchemaTests : DatabaseTestBase
{
    [Test]
    public void DefaultEmojiHaveUniqueNamesAndNoDiscordId()
    {
        Db.Emojis.Add(new Emoji { Id = Guid.NewGuid(), Name = "👍" });
        Db.SaveChanges();

        Db.Emojis.Add(new Emoji { Id = Guid.NewGuid(), Name = "👍" });
        Assert.Throws<DbUpdateException>(() => Db.SaveChanges());
    }

    [Test]
    public void DeletingReferencedRowFails()
    {
        SeedMessage();
        // No cascade deletes: removing a profile that has messages must fail.
        Assert.That(() => Db.Database.ExecuteSqlRaw("DELETE FROM Profiles"), Throws.Exception);
    }
}
