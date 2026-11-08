namespace Importer.Tests;

/// <summary>
/// The guarantees the importer leans on: the slow half runs many at a time, the half that writes to sqlite
/// never does, and it still sees everything in the order the messages were read.
/// </summary>
[TestFixture]
public class OrderedPipelineTests
{
    static async IAsyncEnumerable<int> Range(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    [Test]
    public async Task ResultsAreConsumedInSourceOrderEvenWhenTheySlowDownOutOfOrder()
    {
        var consumed = new List<int>();

        await OrderedPipeline.RunAsync(Range(50), 8,
            // Early items finish last, so anything relying on completion order would come out shuffled.
            async (i, _) => { await Task.Delay(i < 10 ? 40 : 1); return i; },
            i => { consumed.Add(i); return Task.CompletedTask; });

        Assert.That(consumed, Is.EqualTo(Enumerable.Range(0, 50).ToList()));
    }

    [Test]
    public async Task PreparingRunsInParallelUpToTheLimitAndNeverOverIt()
    {
        var running = 0;
        var peak = 0;

        await OrderedPipeline.RunAsync(Range(40), 6,
            async (i, _) =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, now);
                await Task.Delay(10);
                Interlocked.Decrement(ref running);
                return i;
            },
            _ => Task.CompletedTask);

        Assert.That(peak, Is.GreaterThan(1), "nothing ran in parallel");
        Assert.That(peak, Is.LessThanOrEqualTo(6));
    }

    [Test]
    public async Task ConsumingNeverOverlapsBecauseSqliteOnlyTakesOneWriter()
    {
        var inConsumer = 0;
        var overlapped = false;

        await OrderedPipeline.RunAsync(Range(40), 8,
            (i, _) => Task.FromResult(i),
            async _ =>
            {
                if (Interlocked.Increment(ref inConsumer) > 1) overlapped = true;
                await Task.Delay(1);
                Interlocked.Decrement(ref inConsumer);
            });

        Assert.That(overlapped, Is.False);
    }

    [Test]
    public void AFailedPreparationStopsTheRunAndIsReportedAsItself()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => OrderedPipeline.RunAsync(Range(40), 4,
            async (i, _) =>
            {
                await Task.Yield();
                if (i == 5) throw new InvalidOperationException("could not download attachment");
                return i;
            },
            _ => Task.CompletedTask));

        Assert.That(ex!.Message, Is.EqualTo("could not download attachment"));
    }

    [Test]
    public async Task AFailedWriteStopsTheRunSoResumingPicksUpFromACleanPrefix()
    {
        var consumed = new List<int>();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => OrderedPipeline.RunAsync(Range(200), 8,
            async (i, ct) => { await Task.Delay(1, ct); return i; },
            i =>
            {
                if (i == 7) throw new InvalidOperationException("db is unhappy");
                consumed.Add(i);
                return Task.CompletedTask;
            }));

        Assert.That(ex!.Message, Is.EqualTo("db is unhappy"));
        // Everything before the failure was written, nothing after it, so MAX(DiscordId) has no gap behind it.
        Assert.That(consumed, Is.EqualTo(Enumerable.Range(0, 7).ToList()));

        // Nothing left running should surface later as an unobserved task exception.
        await Task.Yield();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [Test]
    public void ParallelismBelowOneIsRejectedRatherThanSilentlyStalling() =>
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            OrderedPipeline.RunAsync(Range(1), 0, (i, _) => Task.FromResult(i), _ => Task.CompletedTask));

    static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)))
            if (Interlocked.CompareExchange(ref target, value, seen) == seen)
                return;
    }
}
