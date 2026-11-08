namespace Importer.Tests;

[TestFixture]
public class LinkClassifierTests
{
    ILinkClassifier _classifier = null!;

    [SetUp]
    public void SetUp() => _classifier = new LinkClassifier();

    [TestCase("https://www.youtube.com/watch?v=abc", "youtube", LinkKind.Track)]
    [TestCase("https://youtu.be/abc", "youtube", LinkKind.Track)]
    [TestCase("https://music.youtube.com/watch?v=abc", "youtube music", LinkKind.Track)]
    [TestCase("https://www.youtube.com/shorts/abc", "youtube shorts", LinkKind.Track)]
    [TestCase("https://www.youtube.com/playlist?list=PL1", "youtube", LinkKind.Playlist)]
    [TestCase("https://www.youtube.com/@somechannel", "youtube", LinkKind.Unsupported)]
    [TestCase("https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC?si=x", "spotify", LinkKind.Track)]
    [TestCase("https://open.spotify.com/intl-de/track/4uLU6hMCjMI75M1A2tKUQC", "spotify", LinkKind.Track)]
    [TestCase("https://open.spotify.com/playlist/37i9dQZF1DX", "spotify", LinkKind.Playlist)]
    [TestCase("https://open.spotify.com/episode/512ojhOuo1ktJprKbVcKyQ", "spotify", LinkKind.Episode)]
    [TestCase("https://open.spotify.com/album/abc", "spotify", LinkKind.Unsupported)]
    [TestCase("https://music.apple.com/us/album/x/1?i=2", "apple music", LinkKind.Track)]
    [TestCase("https://music.apple.com/us/song/halo/123456", "apple music", LinkKind.Track)]
    [TestCase("https://music.apple.com/us/album/x/1", "apple music", LinkKind.Unsupported)]
    [TestCase("https://www.deezer.com/us/track/3135556", "deezer", LinkKind.Track)]
    [TestCase("https://www.deezer.com/playlist/1234", "deezer", LinkKind.Playlist)]
    [TestCase("https://soundcloud.com/a/sets/b", "soundcloud", LinkKind.Playlist)]
    [TestCase("https://artist.bandcamp.com/track/x", "bandcamp", LinkKind.Generic)]
    [TestCase("https://soundcloud.com/a/b", "soundcloud", LinkKind.Generic)]
    [TestCase("https://music.amazon.co.uk/albums/B0", "amazon music", LinkKind.Generic)]
    public void Classifies(string url, string source, LinkKind kind)
    {
        var info = _classifier.Classify(url);
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Source, Is.EqualTo(source));
        Assert.That(info.Kind, Is.EqualTo(kind));
    }

    [TestCase("https://twitter.com/a/status/1")]
    [TestCase("https://tenor.com/view/x")]
    [TestCase("https://i.imgur.com/a.png")]
    [TestCase("https://notyoutube.com/watch?v=abc")]
    public void IgnoresOtherHosts(string url) => Assert.That(_classifier.Classify(url), Is.Null);

    [TestCase("https://spotify.link/abc")]
    [TestCase("https://bit.ly/abc")]
    [TestCase("https://t.co/abc")]
    [TestCase("https://apple.co/abc")]
    [TestCase("https://lnk.to/abc")]
    public void ShortenersAreFollowedRatherThanDropped(string url) =>
        Assert.That(_classifier.Classify(url)!.Kind, Is.EqualTo(LinkKind.ShortLink));
}
