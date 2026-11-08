using System.Net;
using System.Text.Json;

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

/// <summary>
/// What the client does when Spotify says "too many requests". The importer prepares many messages at once
/// and they all share one app-wide quota, so getting this wrong stalls the whole run.
/// </summary>
[TestFixture]
public class SpotifyClientRateLimitTests
{
    /// <summary>Answers the token request, then whatever the test queued for the api calls.</summary>
    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<int, HttpResponseMessage> _reply;
        int _apiCalls;
        public int ApiCalls => _apiCalls;
        public int Peak;
        int _inFlight;

        public StubHandler(Func<int, HttpResponseMessage> reply) => _reply = reply;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "accounts.spotify.com")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"tok","expires_in":3600}"""),
                };

            var now = Interlocked.Increment(ref _inFlight);
            Peak = Math.Max(Peak, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref _inFlight);
            return _reply(Interlocked.Increment(ref _apiCalls));
        }
    }

    static HttpResponseMessage TooManyRequests(TimeSpan retryAfter)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        resp.Headers.Add("Retry-After", ((int)retryAfter.TotalSeconds).ToString());
        return resp;
    }

    static HttpResponseMessage Found() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"tracks":{"items":[]}}"""),
    };

    [Test]
    public void AnAbsurdRetryAfterIsReportedInsteadOfSleptThrough()
    {
        // This is the real failure: Spotify answers a badly over-quota app with hours, and waiting on that
        // is indistinguishable from a hang. It has to come back as an error the user can act on.
        var handler = new StubHandler(_ => TooManyRequests(TimeSpan.FromHours(21)));
        var client = new SpotifyClient(new HttpClient(handler), "id", "secret", "US",
            maxConcurrentRequests: 2, maxBackoff: TimeSpan.FromMinutes(10));

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => client.SearchTracksAsync("creep"));

        Assert.That(ex!.Message, Does.Contain("21.0h"));
        Assert.That(ex.Message, Does.Contain("SpotifyMinIntervalMs"));
        Assert.That(handler.ApiCalls, Is.EqualTo(1), "it should give up, not keep retrying");
    }

    [Test]
    public async Task AShortRetryAfterIsWaitedOutAndTheCallSucceeds()
    {
        var handler = new StubHandler(call => call == 1 ? TooManyRequests(TimeSpan.FromSeconds(1)) : Found());
        var client = new SpotifyClient(new HttpClient(handler), "id", "secret", "US");

        Assert.That(await client.SearchTracksAsync("creep"), Is.Empty);
        Assert.That(handler.ApiCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task ConcurrentLookupsAreHeldToTheRequestLimitBecauseTheQuotaIsAppWide()
    {
        var handler = new StubHandler(_ => Found());
        var client = new SpotifyClient(new HttpClient(handler), "id", "secret", "US", maxConcurrentRequests: 2);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => client.SearchTracksAsync($"q{i}")));

        Assert.That(handler.ApiCalls, Is.EqualTo(10));
        Assert.That(handler.Peak, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public async Task AfterBeingToldOffTheClientPacesItselfInsteadOfChargingStraightBackIn()
    {
        var handler = new StubHandler(call => call == 1 ? TooManyRequests(TimeSpan.FromSeconds(1)) : Found());
        var client = new SpotifyClient(new HttpClient(handler), "id", "secret", "US");

        await client.SearchTracksAsync("creep");
        var started = DateTime.UtcNow;
        for (var i = 0; i < 4; i++) await client.SearchTracksAsync($"q{i}");
        var elapsed = DateTime.UtcNow - started;

        // Four calls at the 300ms gap a single 429 introduces cannot finish instantly.
        Assert.That(elapsed, Is.GreaterThan(TimeSpan.FromMilliseconds(600)));
    }

    [Test]
    public async Task TheSameSearchIsOnlySentOnceHoweverOftenItIsAsked()
    {
        // A song of the day channel reposts artists constantly, and one entry tries several phrasings of the
        // same title, so without this a multi-thousand message import spends its quota asking twice.
        var handler = new StubHandler(_ => Found());
        var client = new SpotifyClient(new HttpClient(handler), "id", "secret", "US",
            minInterval: TimeSpan.Zero);

        for (var i = 0; i < 5; i++) await client.SearchTracksAsync("radiohead creep");
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.SearchTracksAsync("radiohead creep")));

        Assert.That(handler.ApiCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task ArtistGenresAreOnlyFetchedOncePerArtist()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"genres":["shoegaze"]}"""),
        });
        var client = new SpotifyClient(new HttpClient(handler), "id", "secret", "US",
            minInterval: TimeSpan.Zero);

        var first = await client.GetArtistGenresAsync("abc");
        var second = await client.GetArtistGenresAsync("abc");

        Assert.That(handler.ApiCalls, Is.EqualTo(1));
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public async Task AFailedLookupIsNotRememberedAsTheAnswerForever()
    {
        // One transient network error must not poison that query for the rest of a multi-hour run.
        var handler = new StubHandler(call => call == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Found());
        var client = new SpotifyClient(new HttpClient(handler), "id", "secret", "US",
            minInterval: TimeSpan.Zero);

        Assert.ThrowsAsync<HttpRequestException>(() => client.SearchTracksAsync("creep"));
        Assert.That(await client.SearchTracksAsync("creep"), Is.Empty);
        Assert.That(handler.ApiCalls, Is.EqualTo(2), "the retry has to actually reach Spotify again");
    }

    [Test]
    public async Task RequestsArePacedFromTheStartWithoutHavingToBeRefusedFirst()
    {
        // The whole point: the old client began unpaced and could only find the limit by being told off,
        // and every telling off costs a longer penalty than the last.
        var handler = new StubHandler(_ => Found());
        var client = new SpotifyClient(new HttpClient(handler), "id", "secret", "US",
            minInterval: TimeSpan.FromMilliseconds(200));

        var started = DateTime.UtcNow;
        for (var i = 0; i < 4; i++) await client.SearchTracksAsync($"q{i}");
        var elapsed = DateTime.UtcNow - started;

        Assert.That(elapsed, Is.GreaterThan(TimeSpan.FromMilliseconds(500)));
        Assert.That(handler.ApiCalls, Is.EqualTo(4), "pacing must not drop requests");
    }
}
