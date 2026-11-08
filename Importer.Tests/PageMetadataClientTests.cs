namespace Importer.Tests;

[TestFixture]
public class PageMetadataClientTests
{
    [Test]
    public void ReadsJsonLd()
    {
        var html = "<html><script type=\"application/ld+json\">" +
                   "{\"@context\":\"http://schema.org\",\"@type\":\"MusicRecording\",\"name\":\"Halo\"," +
                   "\"duration\":\"PT3M45S\",\"datePublished\":\"2008-11-14\"," +
                   "\"byArtist\":[{\"@type\":\"MusicGroup\",\"name\":\"Beyoncé\"}]}</script></html>";
        var meta = PageMetadataClient.Parse(html);
        Assert.That(meta, Is.Not.Null);
        Assert.That(meta!.Title, Is.EqualTo("Halo"));
        Assert.That(meta.Artist, Is.EqualTo("Beyoncé"));
        Assert.That(meta.DurationSeconds, Is.EqualTo(225));
    }

    [Test]
    public void FallsBackToOpenGraph()
    {
        var meta = PageMetadataClient.Parse("<meta property=\"og:title\" content=\"Halo by Beyoncé on Apple Music\">");
        Assert.That(meta!.Title, Is.EqualTo("Halo"));
        Assert.That(meta.Artist, Is.EqualTo("Beyoncé"));
    }

    [Test]
    public void IgnoresAlbumNodeSoAlbumNameIsNotStoredAsTheSong()
    {
        var html = "<html><script type=\"application/ld+json\">" +
                   "{\"@type\":\"MusicAlbum\",\"name\":\"Greatest Hits\"," +
                   "\"byArtist\":{\"@type\":\"MusicGroup\",\"name\":\"Beyonc\u00e9\"}}</script></html>";
        Assert.That(PageMetadataClient.Parse(html), Is.Null);
    }

    [Test]
    public void PicksTheRequestedTrackOutOfAnAlbum()
    {
        var html = "<html><script type=\"application/ld+json\">" +
                   "{\"@type\":[\"MusicAlbum\",\"Product\"],\"name\":\"I Am... Sasha Fierce\"," +
                   "\"byArtist\":{\"@type\":\"MusicGroup\",\"name\":\"Beyonc\u00e9\"}," +
                   "\"track\":[{\"@type\":\"MusicRecording\",\"name\":\"Single Ladies\",\"url\":\"https://music.apple.com/us/album/x/1?i=111\"}," +
                   "{\"@type\":\"MusicRecording\",\"name\":\"Halo\",\"url\":\"https://music.apple.com/us/album/x/1?i=222\"}]}" +
                   "</script></html>";
        var meta = PageMetadataClient.Parse(html, "222");
        Assert.That(meta!.Title, Is.EqualTo("Halo"));
        Assert.That(meta.Artist, Is.EqualTo("Beyonc\u00e9"));
    }
}
