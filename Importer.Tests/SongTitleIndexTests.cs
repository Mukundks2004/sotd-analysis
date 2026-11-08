using Database.Entities;
using Sotd.Database;

namespace Importer.Tests;

[TestFixture]
public class SongTitleIndexTests : DatabaseTestBase
{
    [Test]
    public async Task LoadsEverySongThatIsAlreadyInTheDatabase()
    {
        var id = Guid.NewGuid();
        Db.Songs.Add(new Song { Id = id, Title = "Creep" });
        await Db.SaveChangesAsync();

        var index = new SongTitleIndex();
        await index.LoadAsync(Db);

        Assert.That(index.Find("Creep"), Is.EqualTo(new[] { id }));
    }

    [Test]
    public void FindIgnoresTheDifferencesNormalizationIsMeantToHide()
    {
        var id = Guid.NewGuid();
        var index = new SongTitleIndex();
        index.Add("Creep", id);

        Assert.That(index.Find("creep - Remastered 2011"), Is.EqualTo(new[] { id }));
        Assert.That(index.Find("Creep (Live at Wembley)"), Is.Empty);
    }

    [Test]
    public void TwoSongsCanShareATitle()
    {
        var index = new SongTitleIndex();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        index.Add("Home", first);
        index.Add("Home", second);

        Assert.That(index.Find("Home"), Is.EquivalentTo(new[] { first, second }));
    }

    [Test]
    public async Task LoadingAgainReplacesTheOldContents()
    {
        var index = new SongTitleIndex();
        index.Add("Creep", Guid.NewGuid());
        await index.LoadAsync(Db);

        Assert.That(index.Find("Creep"), Is.Empty);
    }
}
