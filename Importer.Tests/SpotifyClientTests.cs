using System.Text.Json;
using Sotd.Importer;

namespace Importer.Tests;

[TestFixture]
public class SpotifyClientTests
{
    static SpotifyTrack? Item(string json) =>
        SpotifyClient.ParsePlaylistItem(JsonDocument.Parse(json).RootElement);

    [TestCase("2020-05-17", 2020, 5, 17)]
    [TestCase("2020-05", 2020, 5, 1)]
    [TestCase("1999", 1999, 1, 1)]
    public void ReleaseDates_AreReadAtWhateverPrecisionSpotifyGives(string s, int y, int m, int d) =>
        Assert.That(SpotifyClient.ParseReleaseDate(s), Is.EqualTo(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc)));

    [Test]
    public void PlaylistTrack_IsReadAsACatalogueTrack()
    {
        var t = Item("""
            {"id":"4uLU6hMCjMI75M1A2tKUQC","type":"track","is_local":false,"name":"Creep",
             "duration_ms":238000,"external_ids":{"isrc":"GBAYE9200051"},
             "artists":[{"id":"4Z8W4fKeB5YxbusRsdQVPb","name":"Radiohead"}],
             "album":{"release_date":"1993-02-22"}}
            """);
        Assert.That(t, Is.Not.Null);
        Assert.That(t!.Kind, Is.EqualTo(SpotifyItemKind.Track));
        Assert.That(t.Name, Is.EqualTo("Creep"));
        Assert.That(t.Isrc, Is.EqualTo("GBAYE9200051"));
        Assert.That(t.DurationSeconds, Is.EqualTo(238));
    }

    [Test]
    public void PlaylistEpisode_IsAnEntryCreditedToItsShow()
    {
        var t = Item("""
            {"id":"512ojhOuo1ktJprKbVcKyQ","type":"episode","name":"Episode 42",
             "duration_ms":3600000,"release_date":"2021-07-03",
             "show":{"name":"Song Exploder","publisher":"Hrishikesh Hirway"}}
            """);
        Assert.That(t, Is.Not.Null);
        Assert.That(t!.Kind, Is.EqualTo(SpotifyItemKind.Episode));
        Assert.That(t.Name, Is.EqualTo("Episode 42"));
        Assert.That(t.Id, Is.EqualTo("512ojhOuo1ktJprKbVcKyQ"));
        Assert.That(t.Artists.Single().Name, Is.EqualTo("Song Exploder"));
        Assert.That(t.DurationSeconds, Is.EqualTo(3600));
        Assert.That(t.ReleaseDate, Is.EqualTo(new DateTime(2021, 7, 3, 0, 0, 0, DateTimeKind.Utc)));
        // An episode is not a recording, so it never carries an ISRC to match on.
        Assert.That(t.Isrc, Is.Null);
    }

    [Test]
    public void PlaylistLocalFile_IsAnEntryWithNoIdAndNoLink()
    {
        var t = Item("""
            {"id":null,"type":"track","is_local":true,"name":"Demo Take 3","duration_ms":125000,
             "external_ids":{},"artists":[{"id":null,"name":"My Band"}],"album":{"release_date":"0000"}}
            """);
        Assert.That(t, Is.Not.Null);
        Assert.That(t!.Kind, Is.EqualTo(SpotifyItemKind.LocalFile));
        Assert.That(t.Name, Is.EqualTo("Demo Take 3"));
        Assert.That(t.Id, Is.Null);
        Assert.That(t.Isrc, Is.Null);
        // Spotify's placeholder album date says nothing about the file.
        Assert.That(t.ReleaseDate, Is.Null);
        Assert.That(t.Artists.Single().Name, Is.EqualTo("My Band"));
        Assert.That(t.DurationSeconds, Is.EqualTo(125));
    }

    [Test]
    public void PlaylistItem_WithoutATitleIsNotAnEntry() =>
        Assert.That(Item("""{"id":null,"is_local":true,"name":"","duration_ms":1000}"""), Is.Null);

    [TestCase("gb-duw-00-00059", "GBDUW0000059")]
    [TestCase("gbduw0000059", "GBDUW0000059")]
    [TestCase("nope", null)]
    [TestCase(null, null)]
    public void IsrcsAreNormalisedBeforeTheyAreUsedAsIdentity(string? raw, string? expected) =>
        Assert.That(SpotifyClient.NormalizeIsrc(raw), Is.EqualTo(expected));
}
