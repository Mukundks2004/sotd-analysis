using Database.Entities;
using Discord;
using Moq;
using Sotd.Database;

namespace Importer.Tests;

[TestFixture]
public class DiscordImporterTests
{
    static IAttachment Attachment(string filename, string? contentType)
    {
        var a = new Mock<IAttachment>();
        a.SetupGet(x => x.Filename).Returns(filename);
        a.SetupGet(x => x.ContentType).Returns(contentType!);
        return a.Object;
    }

    [TestCase("song.mp3", null)]
    [TestCase("clip.MP4", null)]
    [TestCase("take.flac", null)]
    [TestCase("whatever", "audio/ogg")]
    [TestCase("whatever", "video/webm")]
    public void AudioAndVideoUploadsAreTreatedAsSongs(string filename, string? contentType) =>
        Assert.That(DiscordImporter.IsAudioOrVideo(Attachment(filename, contentType)), Is.True);

    [TestCase("meme.png", "image/png")]
    [TestCase("notes.txt", "text/plain")]
    [TestCase("archive.zip", null)]
    public void EverythingElseIsNot(string filename, string? contentType) =>
        Assert.That(DiscordImporter.IsAudioOrVideo(Attachment(filename, contentType)), Is.False);
}

[TestFixture]
public class SotdDbContextFactoryTests : DatabaseTestBase
{
    [Test]
    public void EachCallOpensItsOwnContextOnTheConfiguredFile()
    {
        ISotdDbContextFactory factory = new SotdDbContextFactory(DbPath);

        using var first = factory.Create();
        using var second = factory.Create();

        Assert.That(second, Is.Not.SameAs(first));
        // A failed message must be able to roll back without touching anything else in flight.
        Assert.That(first.Songs.Count(), Is.EqualTo(0));
        Assert.That(second.Songs.Count(), Is.EqualTo(0));
    }
}
