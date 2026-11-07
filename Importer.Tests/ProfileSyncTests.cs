using Discord;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sotd.Importer;

namespace Importer.Tests;

[TestFixture]
public class ProfileSyncTests : DatabaseTestBase
{
    Mock<IFileDownloader> _downloader = null!;
    string _repoRoot = "";

    [SetUp]
    public void SetUpSync()
    {
        _repoRoot = Path.Combine(Path.GetTempPath(), $"sotd-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_repoRoot);
        _downloader = new Mock<IFileDownloader>();
        _downloader.Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
    }

    [TearDown]
    public void RemoveRoot() => Directory.Delete(_repoRoot, recursive: true);

    static Mock<IUser> User(ulong id, string username)
    {
        var user = new Mock<IUser>();
        user.SetupGet(u => u.Id).Returns(id);
        user.SetupGet(u => u.Username).Returns(username);
        user.Setup(u => u.GetAvatarUrl(It.IsAny<ImageFormat>(), It.IsAny<ushort>())).Returns("https://cdn/avatar.png");
        user.Setup(u => u.GetDefaultAvatarUrl()).Returns("https://cdn/default.png");
        return user;
    }

    ProfileSync NewSync(params ulong[] members)
    {
        var sync = new ProfileSync(_downloader.Object, _repoRoot);
        sync.SetMembers(members);
        return sync;
    }

    [Test]
    public async Task FirstSightOfSomeoneCreatesTheirProfile()
    {
        var profile = await NewSync(7).EnsureAsync(Db, User(7, "alice").Object);

        Assert.That(profile.Username, Is.EqualTo("alice"));
        Assert.That(profile.IsCurrentMember, Is.True);
        Assert.That(profile.PicturePath, Does.Contain("7-alice"));
        Assert.That(await Db.Profiles.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task SomeoneWhoHasLeftIsNotMarkedAsAMember()
    {
        var profile = await NewSync().EnsureAsync(Db, User(7, "alice").Object);

        Assert.That(profile.IsCurrentMember, Is.False);
    }

    [Test]
    public async Task APictureIsOnlyFetchedOncePerRunNoMatterHowManyMessagesSomeonePosted()
    {
        var sync = NewSync(7);
        var user = User(7, "alice").Object;
        await sync.EnsureAsync(Db, user);
        await sync.EnsureAsync(Db, user);

        _downloader.Verify(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.That(await Db.Profiles.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task AFailedDownloadStillLeavesAUsableProfile()
    {
        _downloader.Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        var profile = await NewSync(7).EnsureAsync(Db, User(7, "alice").Object);

        Assert.That(profile.PicturePath, Is.Empty);
        Assert.That(profile.Username, Is.EqualTo("alice"));
    }

    [Test]
    public async Task MembershipSyncMarksEveryoneWhoIsNoLongerInTheGuild()
    {
        await NewSync(7, 8).EnsureAsync(Db, User(7, "alice").Object);
        await NewSync(7, 8).EnsureAsync(Db, User(8, "bob").Object);

        var later = NewSync(7);
        await later.SyncMembershipAsync(Db);

        var members = await Db.Profiles.OrderBy(p => p.DiscordId)
            .Select(p => new { p.Username, p.IsCurrentMember }).ToListAsync();
        Assert.That(members.Single(m => m.Username == "alice").IsCurrentMember, Is.True);
        Assert.That(members.Single(m => m.Username == "bob").IsCurrentMember, Is.False);
    }
}
