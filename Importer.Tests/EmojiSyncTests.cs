using Discord;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Importer.Tests;

[TestFixture]
public class EmojiSyncTests : DatabaseTestBase
{
    Mock<IFileDownloader> _downloader = null!;
    EmojiSync _sync = null!;
    string _repoRoot = "";

    [SetUp]
    public void SetUpSync()
    {
        _repoRoot = Path.Combine(Path.GetTempPath(), $"sotd-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_repoRoot);
        _downloader = new Mock<IFileDownloader>();
        _downloader.Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _sync = new EmojiSync(_downloader.Object, _repoRoot);
    }

    [TearDown]
    public void RemoveRoot() => Directory.Delete(_repoRoot, recursive: true);

    [Test]
    public async Task ACustomEmojiIsStoredOnceWithItsPicture()
    {
        var emote = Emote.Parse("<:party:123456789>");

        var first = await _sync.EnsureAsync(Db, emote);
        var second = await _sync.EnsureAsync(Db, emote);

        Assert.That(second.Id, Is.EqualTo(first.Id));
        Assert.That(first.DiscordId, Is.EqualTo(123456789));
        Assert.That(first.Path, Does.Contain("123456789-party"));
        Assert.That(await Db.Emojis.CountAsync(), Is.EqualTo(1));
        _downloader.Verify(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task AUnicodeEmojiIsStoredByNameAndNeverDownloaded()
    {
        var emoji = await _sync.EnsureAsync(Db, new Discord.Emoji("👍"));

        Assert.That(emoji.DiscordId, Is.Null);
        Assert.That(emoji.Name, Is.EqualTo("👍"));
        Assert.That(await _sync.EnsureAsync(Db, new Discord.Emoji("👍")), Has.Property("Id").EqualTo(emoji.Id));
        _downloader.Verify(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task AnAnimatedEmojiIsRecordedAsAnimated()
    {
        var emoji = await _sync.EnsureAsync(Db, Emote.Parse("<a:dance:987654321>"));

        Assert.That(emoji.IsAnimated, Is.True);
        Assert.That(emoji.Path, Does.EndWith(".gif"));
    }
}
