using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace Sotd.Importer;

/// <summary>
/// Runs the slow half of a job for many items at once while handing the results to the second half strictly
/// one at a time, in the order the items arrived.
///
/// The importer needs exactly this shape. Nearly all of its time goes on the network (Spotify, iTunes, Deezer,
/// yt-dlp, page metadata, attachment downloads, reaction lists) and that part parallelises freely. Writing,
/// on the other hand, cannot be done out of order: sqlite accepts one writer at a time, a reply needs the
/// message it answers to be stored already, song de-duplication depends on the order songs are first seen,
/// and resuming a run keys off the highest message id stored, so a gap left by a crash would be skipped
/// forever. Preparing out of order and consuming in order keeps every one of those guarantees.
/// </summary>
public static class OrderedPipeline
{
    /// <param name="maxParallelism">How many <paramref name="prepare"/> calls may be in flight at once. 1 is fully sequential.</param>
    /// <param name="prepare">The parallel half. Must not touch anything the consumer owns.</param>
    /// <param name="consume">The serial half. Called for one item at a time, in source order.</param>
    public static async Task RunAsync<TIn, TOut>(
        IAsyncEnumerable<TIn> source,
        int maxParallelism,
        Func<TIn, CancellationToken, Task<TOut>> prepare,
        Func<TOut, Task> consume,
        CancellationToken cancellationToken = default)
    {
        if (maxParallelism < 1) throw new ArgumentOutOfRangeException(nameof(maxParallelism));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = cts.Token;
        using var throttle = new SemaphoreSlim(maxParallelism, maxParallelism);

        // Holds started work, not finished results, so the consumer can await them in order. Bounding it is
        // what stops the producer reading five years of messages into memory ahead of the writer.
        var queue = Channel.CreateBounded<Task<TOut>>(new BoundedChannelOptions(Math.Max(2, maxParallelism * 4))
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in source.WithCancellation(ct))
                {
                    await throttle.WaitAsync(ct);
                    Task<TOut> work;
                    try
                    {
                        work = ReleaseWhenDone(prepare(item, ct), throttle);
                    }
                    catch
                    {
                        throttle.Release();
                        throw;
                    }

                    try
                    {
                        await queue.Writer.WriteAsync(work, ct);
                    }
                    catch
                    {
                        // Never enqueued, so nobody else will ever await it.
                        try { await work; } catch { /* the failure that stopped us is the one that matters */ }
                        throw;
                    }
                }
            }
            finally
            {
                queue.Writer.TryComplete();
            }
        }, CancellationToken.None);

        Exception? failure = null;
        try
        {
            await foreach (var work in queue.Reader.ReadAllAsync(CancellationToken.None))
                await consume(await work);
        }
        catch (Exception ex)
        {
            failure = ex;
            cts.Cancel();
        }

        if (failure != null)
        {
            // Observe whatever was already in flight so it cannot resurface as an unobserved task exception.
            while (queue.Reader.TryRead(out var abandoned))
                try { await abandoned; } catch { /* ignored */ }
        }

        try
        {
            await producer;
        }
        catch when (failure != null)
        {
            // The consumer failed first and we cancelled the producer ourselves; its complaint is noise.
        }

        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    static async Task<T> ReleaseWhenDone<T>(Task<T> work, SemaphoreSlim throttle)
    {
        try { return await work; }
        finally { throttle.Release(); }
    }
}
