using Database;
using Discord;
using Discord.Rest;
using Microsoft.EntityFrameworkCore;
using Attachment = Sotd.Database.Attachment;

namespace Importer;

/// <summary>An attachment that has been downloaded and, if it is audio or video, already identified.</summary>
public sealed record PreparedAttachment(IAttachment Source, string RelativePath, string Extension, ResolvedMedia? Media);

/// <summary>One emote on a message together with everyone who reacted with it.</summary>
public sealed record PreparedReaction(IEmote Emote, List<IUser> Users);

/// <summary>
/// A message with all of its slow network work already done, ready to be written to the database.
/// Preparing these in parallel is what makes the importer fast; they are still written one at a time
/// in message order so ordering, resume and song de-duplication behave exactly as they did serially.
/// </summary>
public sealed record PreparedMessage(
    IMessage Message,
    string Context,
    List<ResolvedMedia> LinkMedia,
    List<PreparedAttachment> Attachments,
    List<PreparedReaction> Reactions);

public class DiscordImporter : IDiscordImporter
{
    static readonly HashSet<string> AudioVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "m4a", "wav", "flac", "ogg", "oga", "opus", "aac", "wma", "aif", "aiff", "mid",
        "mp4", "m4v", "mov", "mkv", "webm", "avi", "wmv", "flv", "mpeg", "mpg", "3gp",
    };

    readonly ImporterConfig _config;
    readonly string _repoRoot;
    readonly ISotdDbContextFactory _dbs;
    readonly IFileDownloader _downloader;
    readonly IMediaResolver _resolver;
    readonly IProfileSync _profiles;
    readonly IEmojiSync _emojis;
    readonly ISongTitleIndex _songIndex;
    readonly ICatalogWriterFactory _writers;

    public DiscordImporter(ImporterConfig config, string repoRoot, ISotdDbContextFactory dbs,
        IFileDownloader downloader, IMediaResolver resolver, IProfileSync profiles, IEmojiSync emojis,
        ISongTitleIndex songIndex, ICatalogWriterFactory writers)
    {
        _config = config;
        _repoRoot = repoRoot;
        _dbs = dbs;
        _downloader = downloader;
        _resolver = resolver;
        _profiles = profiles;
        _emojis = emojis;
        _songIndex = songIndex;
        _writers = writers;
    }

    SotdDbContext NewDb() => _dbs.Create();

    public async Task RunAsync()
    {
        using var rest = new DiscordRestClient();
        await rest.LoginAsync(TokenType.Bot, _config.DiscordToken);

        var guild = await rest.GetGuildAsync(_config.GuildId)
                    ?? throw new InvalidOperationException($"Bot cannot see guild {_config.GuildId}. Is it in the server?");

        Console.WriteLine($"Reading member list of '{guild.Name}'...");
        var members = new List<IGuildUser>();
        await foreach (var page in guild.GetUsersAsync())
            members.AddRange(page);
        var memberIds = members.Select(m => m.Id).ToHashSet();
        Console.WriteLine($"  {members.Count} members");

        _profiles.SetMembers(memberIds);
        using (var db = NewDb())
            await _songIndex.LoadAsync(db);

        // Refresh profiles we already have that are still in the server.
        using (var db = NewDb())
        {
            var known = (await db.Profiles.Select(p => p.DiscordId).ToListAsync()).Select(x => (ulong)x).ToHashSet();
            foreach (var member in members.Where(m => known.Contains(m.Id)))
                await _profiles.EnsureAsync(db, member);
        }

        foreach (var channelId in _config.ChannelIds)
            await ImportChannelAsync(rest, channelId);

        using (var db = NewDb())
            await _profiles.SyncMembershipAsync(db);
        Console.WriteLine("Done.");
    }

    async Task ImportChannelAsync(DiscordRestClient rest, ulong channelId)
    {
        var channel = await rest.GetChannelAsync(channelId) as IMessageChannel
                      ?? throw new InvalidOperationException($"Channel {channelId} not found or is not a message channel.");

        Guid dbChannelId;
        long lastStored;
        using (var db = NewDb())
        {
            var did = (long)channelId;
            var row = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == did);
            if (row == null)
            {
                row = new Database.Channel { Id = Guid.NewGuid(), DiscordId = did, Name = channel.Name };
                db.Channels.Add(row);
            }
            else
            {
                row.Name = channel.Name;
            }
            await db.SaveChangesAsync();
            dbChannelId = row.Id;
            lastStored = await db.Messages.Where(m => m.ChannelId == dbChannelId).MaxAsync(m => (long?)m.DiscordId) ?? 0;
        }

        Console.WriteLine(lastStored == 0
            ? $"#{channel.Name}: importing from the beginning"
            : $"#{channel.Name}: resuming after message {lastStored}");

        int messages = 0, entries = 0;
        var last = (ulong)lastStored;

        // Walking the channel is cheap; identifying what was posted is not. So the messages stream out in
        // order here and OrderedPipeline does the identifying many at a time, while still writing in order.
        async IAsyncEnumerable<IMessage> NewMessagesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var page in channel.GetMessagesAsync(last, Direction.After, int.MaxValue).WithCancellation(ct))
            {
                foreach (var msg in page.OrderBy(m => m.Id))
                {
                    if (msg.Id <= last) continue;
                    last = msg.Id;
                    if (msg.Type is not (MessageType.Default or MessageType.Reply)) continue;
                    yield return msg;
                }
            }
        }

        await OrderedPipeline.RunAsync(
            NewMessagesAsync(),
            _config.MaxParallelism,
            (msg, ct) => PrepareMessageAsync(msg, channel.Name, ct),
            async prepared =>
            {
                entries += await WriteMessageAsync(prepared, dbChannelId);
                messages++;
                // The first one is worth announcing: on a fresh run it is the proof that writing has started.
                if (messages == 1 || messages % 50 == 0)
                    Console.WriteLine($"  #{channel.Name}: {messages} messages, {entries} entries (at {prepared.Message.Timestamp:yyyy-MM-dd})");
            });

        Console.WriteLine($"#{channel.Name}: imported {messages} messages, {entries} entries");
    }

    /// <summary>
    /// Everything about a message that needs the network: resolving its links, downloading and identifying its
    /// attachments and listing who reacted. Touches no database state, so many of these can run at once.
    /// </summary>
    async Task<PreparedMessage> PrepareMessageAsync(IMessage msg, string channelName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var context = $"#{channelName} message {msg.Id}";

        var attachments = new List<PreparedAttachment>();
        foreach (var att in msg.Attachments)
        {
            var relative = Text.RelativePath("Download", "Attachments", $"{att.Id}-{Text.SanitizeFileName(att.Filename)}");
            var full = Path.Combine(_repoRoot, relative);
            if (!await _downloader.TryDownloadAsync(att.Url, full))
                throw new InvalidOperationException($"Could not download attachment {att.Filename} ({att.Id}) of {context}");

            var media = IsAudioOrVideo(att) ? await _resolver.ResolveFileAsync(full, att.Filename, context) : null;
            var ext = Path.GetExtension(att.Filename).TrimStart('.').ToLowerInvariant();
            attachments.Add(new PreparedAttachment(att, relative, ext, media));
        }

        var reactions = new List<PreparedReaction>();
        if (msg is IUserMessage userMessage && userMessage.Reactions.Count > 0)
        {
            foreach (var emote in userMessage.Reactions.Keys)
            {
                var users = new List<IUser>();
                await foreach (var page in userMessage.GetReactionUsersAsync(emote, 1000).WithCancellation(ct))
                    users.AddRange(page);
                reactions.Add(new PreparedReaction(emote, users));
            }
        }

        var linkMedia = new List<ResolvedMedia>();
        foreach (var link in Text.ExtractLinks(msg.Content))
            linkMedia.AddRange(await _resolver.ResolveLinkAsync(link, context));

        return new PreparedMessage(msg, context, linkMedia, attachments, reactions);
    }

    /// <summary>Writes one prepared message and everything attached to it in a single transaction. Returns the number of entries added.</summary>
    async Task<int> WriteMessageAsync(PreparedMessage prepared, Guid channelId)
    {
        var msg = prepared.Message;
        using var db = NewDb();
        var discordId = (long)msg.Id;
        if (await db.Messages.AnyAsync(m => m.DiscordId == discordId)) return 0;

        await using var tx = await db.Database.BeginTransactionAsync();

        var author = await _profiles.EnsureAsync(db, msg.Author);

        Guid? replyId = null;
        if (msg.Reference?.MessageId.IsSpecified == true)
        {
            var replyDiscordId = (long)msg.Reference.MessageId.Value;
            replyId = await db.Messages
                .Where(m => m.DiscordId == replyDiscordId && m.ChannelId == channelId)
                .Select(m => (Guid?)m.Id).FirstOrDefaultAsync();
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            DiscordId = discordId,
            ChannelId = channelId,
            PostedById = author.Id,
            PostedAt = msg.Timestamp.UtcDateTime,
            EditedAt = msg.EditedTimestamp?.UtcDateTime,
            Content = msg.Content ?? "",
            ReplyId = replyId,
        };
        db.Messages.Add(message);

        var attachments = new List<(Attachment Row, PreparedAttachment Prepared)>();
        foreach (var att in prepared.Attachments)
        {
            var fileExt = await GetOrCreateExtensionAsync(db, att.Extension);
            var row = new Attachment
            {
                Id = Guid.NewGuid(),
                DiscordId = (long)att.Source.Id,
                MessageId = message.Id,
                Filename = att.Source.Filename,
                FileExtensionId = fileExt.Id,
                Size = att.Source.Size,
                Path = att.RelativePath,
            };
            db.Attachments.Add(row);
            attachments.Add((row, att));
        }
        await db.SaveChangesAsync();

        await AddReactionsAsync(db, prepared, message);

        var writer = _writers.Create(db);
        var entryCount = 0;
        foreach (var media in prepared.LinkMedia)
        {
            await writer.AddEntryAsync(message, media, null, prepared.Context);
            entryCount++;
        }

        foreach (var (row, att) in attachments)
        {
            if (att.Media == null) continue;
            await writer.AddEntryAsync(message, att.Media, row, prepared.Context);
            entryCount++;
        }

        await tx.CommitAsync();
        return entryCount;
    }

    async Task AddReactionsAsync(SotdDbContext db, PreparedMessage prepared, Message message)
    {
        if (prepared.Reactions.Count == 0) return;

        var seen = new HashSet<(Guid Profile, Guid Emoji)>();
        foreach (var reaction in prepared.Reactions)
        {
            var emoji = await _emojis.EnsureAsync(db, reaction.Emote);
            foreach (var user in reaction.Users)
            {
                var profile = await _profiles.EnsureAsync(db, user);
                if (seen.Add((profile.Id, emoji.Id)))
                    db.Reactions.Add(new Reaction { MessageId = message.Id, ProfileId = profile.Id, EmojiId = emoji.Id });
            }
            await db.SaveChangesAsync();
        }
    }

    static async Task<FileExtension> GetOrCreateExtensionAsync(SotdDbContext db, string name)
    {
        var ext = await db.FileExtensions.FirstOrDefaultAsync(e => e.Name == name);
        if (ext != null) return ext;
        ext = new FileExtension { Id = Guid.NewGuid(), Name = name };
        db.FileExtensions.Add(ext);
        await db.SaveChangesAsync();
        return ext;
    }

    public static bool IsAudioOrVideo(IAttachment a)
    {
        if (a.ContentType is { } ct &&
            (ct.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) || ct.StartsWith("video/", StringComparison.OrdinalIgnoreCase)))
            return true;
        return AudioVideoExtensions.Contains(Path.GetExtension(a.Filename).TrimStart('.'));
    }
}
