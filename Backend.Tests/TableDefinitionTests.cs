using Backend.Services;
using Sotd.Backend.Dtos;
using Sotd.Database;

namespace Backend.Tests;

/// <summary>
/// What each table says about itself. The CRUD service trusts these completely, so the awkward cases
/// (unique columns that must be null when blank, snowflakes, UTC) are checked here rather than per table.
/// </summary>
[TestFixture]
public class TableDefinitionTests
{
    [Test]
    public void BlankUniqueColumnsAreStoredAsNullSoTwoEmptyRowsDoNotCollide()
    {
        var song = new SongTable().NewEntity(new SongWrite(" Creep ", "   ", ""));

        Assert.That(song.Title, Is.EqualTo("Creep"));
        Assert.That(song.SpotifyId, Is.Null);
        Assert.That(song.Isrc, Is.Null);
    }

    [Test]
    public void UpdatingKeepsTheExistingIdentity()
    {
        var table = new SongTable();
        var song = new Song { Id = Guid.NewGuid(), Title = "old" };
        var id = song.Id;

        table.ApplyWrite(new SongWrite("new", "sp1", null), song);

        Assert.That(song.Id, Is.EqualTo(id));
        Assert.That(song.Title, Is.EqualTo("new"));
        Assert.That(song.SpotifyId, Is.EqualTo("sp1"));
    }

    [Test]
    public void SnowflakesSurviveAsStringsInBothDirections()
    {
        var table = new ProfileTable();
        const string id = "9007199254740993";

        var profile = table.NewEntity(new ProfileWrite(id, " alice ", true, null));

        Assert.That(profile.DiscordId, Is.EqualTo(9007199254740993L));
        Assert.That(profile.Username, Is.EqualTo("alice"));
        // A missing picture is stored as empty rather than null, matching the entity's own default.
        Assert.That(profile.PicturePath, Is.Empty);
        Assert.That(table.ToRead(profile).DiscordId, Is.EqualTo(id));
    }

    [Test]
    public void ThePictureColumnIsSortableButNeverSearched()
    {
        var columns = new ProfileTable().Columns;

        Assert.That(columns.Select(c => c.Name), Does.Contain("picturePath"));
        Assert.That(columns.Single(c => c.Name == "picturePath").IsText, Is.False);
        Assert.That(columns.Where(c => c.IsText).Select(c => c.Name), Is.EqualTo(new[] { "username" }));
    }

    [Test]
    public void MessageTimestampsAreForcedToUtc()
    {
        var message = new MessageTable().NewEntity(new MessageWrite("1", Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified), null, null, null));

        Assert.That(message.PostedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        // Content is never null, so the filter bar never has to deal with one.
        Assert.That(message.Content, Is.Empty);
    }

    [Test]
    public void MessagesShowNewestFirstBecauseThatIsWhatTheUiOpensOn()
    {
        var table = new MessageTable();

        Assert.That(table.DefaultColumn, Is.EqualTo("postedAt"));
        Assert.That(table.DefaultDesc, Is.True);
        Assert.That(table.IsBig, Is.True);
    }

    [Test]
    public void AnEmojiWithNoDiscordIdIsOneOfDiscordsOwn()
    {
        var emoji = new EmojiTable().NewEntity(new EmojiWrite("👍", null, false, null));

        Assert.That(emoji.DiscordId, Is.Null);
        Assert.That(emoji.Path, Is.Null);
    }

    [Test]
    public void EveryTableCanOrderByItsOwnDefaultColumn()
    {
        // A default column that is not in Columns would silently order by nothing at all.
        AssertDefaultIsReal(new PersonTable());
        AssertDefaultIsReal(new ProfileTable());
        AssertDefaultIsReal(new ChannelTable());
        AssertDefaultIsReal(new MessageTable());
        AssertDefaultIsReal(new FileExtensionTable());
        AssertDefaultIsReal(new AttachmentTable());
        AssertDefaultIsReal(new EmojiTable());
        AssertDefaultIsReal(new SongTable());
        AssertDefaultIsReal(new GenreTable());
        AssertDefaultIsReal(new ArtistTable());
        AssertDefaultIsReal(new SourceTable());
        AssertDefaultIsReal(new SotdEntryTable());
    }

    static void AssertDefaultIsReal<TEntity, TRead, TWrite>(ITableDefinition<TEntity, TRead, TWrite> table)
        where TEntity : class =>
        Assert.That(table.Columns.Select(c => c.Name), Does.Contain(table.DefaultColumn),
            $"{table.GetType().Name} orders by a column it does not declare.");
}
