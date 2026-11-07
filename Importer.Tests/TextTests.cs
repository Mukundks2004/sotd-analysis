using Sotd.Importer;

namespace Importer.Tests;

[TestFixture]
public class TextTests
{
    [Test]
    public void ExtractLinks_TrimsPunctuationAndDedupes()
    {
        var links = Text.ExtractLinks("check this https://youtu.be/abc123. and (https://open.spotify.com/track/xyz) <https://a.com/x> https://youtu.be/abc123");
        Assert.That(links, Is.EqualTo(new[] { "https://youtu.be/abc123", "https://open.spotify.com/track/xyz", "https://a.com/x" }));
    }

    [Test]
    public void NormalizeTitle_IgnoresCaseAccentsPunctuation() =>
        Assert.That(Text.NormalizeTitle("  Beyoncé -- HALO!! "), Is.EqualTo("beyonce halo"));

    [Test]
    public void CleanVideoTitle_StripsNoise() =>
        Assert.That(Text.CleanVideoTitle("Radiohead - Creep (Official Video) [HD]"), Is.EqualTo("Radiohead - Creep"));

    [Test]
    public void SanitizeFileName_KeepsExtensionAndRemovesBadChars()
    {
        Assert.That(Text.SanitizeFileName("a:b?c.mp3"), Is.EqualTo("a_b_c.mp3"));
        var longName = Text.SanitizeFileName(new string('x', 300) + ".flac");
        Assert.That(longName, Does.EndWith(".flac"));
        Assert.That(longName.Length, Is.LessThanOrEqualTo(105));
    }

    [Test]
    public void TryParseArtistTitle_Splits() =>
        Assert.That(Text.TryParseArtistTitle("Radiohead - Creep"), Is.EqualTo(("Radiohead", "Creep")));

    [TestCase("Radiohead - Creep | Official Music Video", "Radiohead - Creep")]
    [TestCase("Radiohead - Creep - Official Video", "Radiohead - Creep")]
    [TestCase("【MV】Band - Song", "Band - Song")]
    [TestCase("Artist - Song (prod. by Someone)", "Artist - Song")]
    [TestCase("\"Creep\"", "Creep")]
    public void CleanVideoTitle_StripsUnbracketedAndCjkNoise(string raw, string expected) =>
        Assert.That(Text.CleanVideoTitle(raw), Is.EqualTo(expected));

    [Test]
    public void SplitFeatured_PullsOutCredits()
    {
        var (title, featured) = Text.SplitFeatured("Song Name (feat. Alice & Bob)");
        Assert.That(title, Is.EqualTo("Song Name"));
        Assert.That(featured, Is.EqualTo(new[] { "Alice", "Bob" }));
    }

    [Test]
    public void SplitFeatured_HandlesTrailingFeat()
    {
        var (title, featured) = Text.SplitFeatured("Song Name ft. Alice");
        Assert.That(title, Is.EqualTo("Song Name"));
        Assert.That(featured, Is.EqualTo(new[] { "Alice" }));
    }

    [Test]
    public void ArtistTitleCandidates_OffersBothSplitsOfAnAmbiguousTitle()
    {
        var candidates = Text.ArtistTitleCandidates("Artist - Song - Live at Wembley");
        Assert.That(candidates, Does.Contain(("Artist", "Song - Live at Wembley")));
        Assert.That(candidates, Does.Contain(("Artist - Song", "Live at Wembley")));
    }

    [TestCase("Artist: Song", "Artist", "Song")]
    [TestCase("Artist \"Song\"", "Artist", "Song")]
    public void ArtistTitleCandidates_HandlesOtherShapes(string raw, string artist, string title) =>
        Assert.That(Text.ArtistTitleCandidates(raw), Does.Contain((artist, title)));

    [TestCase("RadioheadVEVO", "Radiohead")]
    [TestCase("Radiohead - Topic", "Radiohead")]
    [TestCase("Radiohead Official Channel", "Radiohead")]
    public void CleanUploader_StripsChannelDecoration(string raw, string expected) =>
        Assert.That(Text.CleanUploader(raw), Is.EqualTo(expected));

    [TestCase("Various Artists")]
    [TestCase("Some Label Records")]
    [TestCase("SomethingVEVO")]
    public void IsLikelyChannelName_RejectsNonArtists(string name) =>
        Assert.That(Text.IsLikelyChannelName(name), Is.True);

    [TestCase("Radiohead")]
    [TestCase("TV Girl")]
    public void IsLikelyChannelName_KeepsRealArtists(string name) =>
        Assert.That(Text.IsLikelyChannelName(name), Is.False);

    [TestCase("Creep - Remastered 2011")]
    [TestCase("Creep (Remastered)")]
    [TestCase("Creep - Radio Edit")]
    [TestCase("Creep (feat. Someone)")]
    public void NormalizeForMatching_IgnoresReissueMarkers(string title) =>
        Assert.That(Text.NormalizeForMatching(title), Is.EqualTo(Text.NormalizeForMatching("Creep")));

    [TestCase("Creep - Live at Wembley")]
    [TestCase("Creep (Remix)")]
    [TestCase("Creep - Acoustic")]
    public void NormalizeForMatching_KeepsDifferentRecordingsApart(string title) =>
        Assert.That(Text.NormalizeForMatching(title), Is.Not.EqualTo(Text.NormalizeForMatching("Creep")));

    [Test]
    public void ContainsAsTokens_DoesNotMatchPartialWords()
    {
        Assert.That(Text.ContainsAsTokens("radiohead creep official video", "creep"), Is.True);
        Assert.That(Text.ContainsAsTokens("homesick blues", "home"), Is.False);
    }
}
