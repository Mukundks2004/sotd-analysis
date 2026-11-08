using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Sotd.Database;

namespace Importer;

/// <summary>The parts of a stored message that processing needs, loaded up front so the parallel half touches no database.</summary>
public sealed record MessageToProcess(
    Guid Id,
    long DiscordId,
    string ChannelName,
    string Content,
    List<AttachmentToProcess> Attachments);

public sealed record AttachmentToProcess(Guid Id, string Filename, string Path);

/// <summary>A message with its links and audio/video attachments already identified, ready to be written.</summary>
public sealed record ProcessedMessage(
    Guid MessageId,
    string Context,
    List<ResolvedMedia> LinkMedia,
    List<(Guid AttachmentId, ResolvedMedia Media)> AttachmentMedia);

/// <summary>
/// The <c>--process</c> run: goes through the messages already in the database that have no SOTD entry yet,
/// resolves their links and uploaded files through the music services, and writes songs, artists, genres,
/// link sources, song links, attachment songs and SOTD entries. Never talks to Discord.
/// </summary>
public class MessageProcessor : IMessageProcessor
{
    const int BatchSize = 200;

    readonly ImporterConfig _config;
    readonly string _repoRoot;
    readonly ISotdDbContextFactory _dbs;
    readonly IMediaResolver _resolver;
    readonly ISongTitleIndex _songIndex;
    readonly ICatalogWriterFactory _writers;

    public MessageProcessor(ImporterConfig config, string repoRoot, ISotdDbContextFactory dbs,
        IMediaResolver resolver, ISongTitleIndex songIndex, ICatalogWriterFactory writers)
    {
        _config = config;
        _repoRoot = repoRoot;
        _dbs = dbs;
        _resolver = resolver;
        _songIndex = songIndex;
        _writers = writers;
    }

    public async Task RunAsync()
    {
        List<Guid> pending;
        using (var db = _dbs.Create())
        {
            await _songIndex.LoadAsync(db);
            // Oldest first, so song de-duplication sees songs in the order they were posted.
            pending = await db.Messages
                .Where(m => !m.SotdEntries.Any())
                .OrderBy(m => m.PostedAt).ThenBy(m => m.DiscordId)
                .Select(m => m.Id)
                .ToListAsync();
        }
        Console.WriteLine($"{pending.Count} messages without entries to process");

        int messages = 0, entries = 0;
        await OrderedPipeline.RunAsync(
            LoadAsync(pending),
            _config.MaxParallelism,
            PrepareAsync,
            async processed =>
            {
                entries += await WriteAsync(processed);
                messages++;
                if (messages == 1 || messages % 50 == 0)
                    Console.WriteLine($"  {messages}/{pending.Count} messages, {entries} entries");
            });

        Console.WriteLine($"Processed {messages} messages, {entries} entries");
    }

    async IAsyncEnumerable<MessageToProcess> LoadAsync(List<Guid> ids)
    {
        foreach (var batch in ids.Chunk(BatchSize))
        {
            List<MessageToProcess> loaded;
            using (var db = _dbs.Create())
            {
                var rows = await db.Messages.AsNoTracking()
                    .Where(m => batch.Contains(m.Id))
                    .Select(m => new
                    {
                        m.Id,
                        m.DiscordId,
                        ChannelName = m.Channel.Name,
                        m.Content,
                        Attachments = m.Attachments.Select(a => new AttachmentToProcess(a.Id, a.Filename, a.Path)).ToList(),
                    })
                    .ToListAsync();
                var byId = rows.ToDictionary(r => r.Id);
                loaded = batch.Where(byId.ContainsKey).Select(id =>
                {
                    var r = byId[id];
                    return new MessageToProcess(r.Id, r.DiscordId, r.ChannelName, r.Content, r.Attachments);
                }).ToList();
            }
            foreach (var m in loaded) yield return m;
        }
    }

    /// <summary>Resolves links and uploaded audio/video files. Touches no database state, so many can run at once.</summary>
    async Task<ProcessedMessage> PrepareAsync(MessageToProcess msg, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var context = $"#{msg.ChannelName} message {msg.DiscordId}";

        var linkMedia = new List<ResolvedMedia>();
        foreach (var link in Text.ExtractLinks(msg.Content))
            linkMedia.AddRange(await _resolver.ResolveLinkAsync(link, context));

        var attachmentMedia = new List<(Guid, ResolvedMedia)>();
        foreach (var att in msg.Attachments.Where(a => DiscordImporter.IsAudioOrVideoFilename(a.Filename)))
        {
            var full = Path.Combine(_repoRoot, att.Path);
            attachmentMedia.Add((att.Id, await _resolver.ResolveFileAsync(full, att.Filename, context)));
        }

        return new ProcessedMessage(msg.Id, context, linkMedia, attachmentMedia);
    }

    /// <summary>Writes every entry of one message in a single transaction. Returns the number of entries added.</summary>
    async Task<int> WriteAsync(ProcessedMessage processed)
    {
        if (processed.LinkMedia.Count == 0 && processed.AttachmentMedia.Count == 0) return 0;

        using var db = _dbs.Create();
        if (await db.SotdEntries.AnyAsync(e => e.MessageId == processed.MessageId)) return 0;

        await using var tx = await db.Database.BeginTransactionAsync();
        var message = await db.Messages.FirstAsync(m => m.Id == processed.MessageId);
        var writer = _writers.Create(db);

        var count = 0;
        foreach (var media in processed.LinkMedia)
        {
            await writer.AddEntryAsync(message, media, null, processed.Context);
            count++;
        }

        foreach (var (attachmentId, media) in processed.AttachmentMedia)
        {
            var attachment = await db.Attachments.FirstAsync(a => a.Id == attachmentId);
            await writer.AddEntryAsync(message, media, attachment, processed.Context);
            count++;
        }

        await tx.CommitAsync();
        return count;
    }
}
