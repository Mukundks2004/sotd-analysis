using Sotd.Importer;

namespace Importer.Tests;

[TestFixture]
public class ITunesClientTests
{
    [Test]
    public void ParsesLookupResponseAndIgnoresTheCollectionRow()
    {
        var json = "{\"resultCount\":2,\"results\":[" +
                   "{\"wrapperType\":\"collection\",\"collectionName\":\"I Am... Sasha Fierce\"}," +
                   "{\"kind\":\"song\",\"trackName\":\"Halo\",\"artistName\":\"Beyonc\u00e9\"," +
                   "\"trackTimeMillis\":261000,\"releaseDate\":\"2008-11-14T08:00:00Z\"}]}";
        var track = ITunesClient.Parse(json);
        Assert.That(track!.Title, Is.EqualTo("Halo"));
        Assert.That(track.Artists, Is.EqualTo(new[] { "Beyonc\u00e9" }));
        Assert.That(track.DurationSeconds, Is.EqualTo(261));
    }
}

[TestFixture]
public class DeezerClientTests
{
    [Test]
    public void ParsesTrackIncludingIsrc()
    {
        var json = "{\"id\":3135556,\"title\":\"Harder Better Faster Stronger\",\"duration\":224," +
                   "\"isrc\":\"GBDUW0000059\",\"release_date\":\"2001-03-07\"," +
                   "\"artist\":{\"name\":\"Daft Punk\"}}";
        var track = DeezerClient.ParseTrack(json);
        Assert.That(track!.Title, Is.EqualTo("Harder Better Faster Stronger"));
        Assert.That(track.Artists, Is.EqualTo(new[] { "Daft Punk" }));
        Assert.That(track.DurationSeconds, Is.EqualTo(224));
        Assert.That(track.Isrc, Is.EqualTo("GBDUW0000059"));
    }

    [Test]
    public void IgnoresErrorPayload() =>
        Assert.That(DeezerClient.ParseTrack("{\"error\":{\"type\":\"DataException\"}}"), Is.Null);
}
