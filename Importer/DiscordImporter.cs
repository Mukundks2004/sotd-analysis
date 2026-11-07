using Discord;
using Discord.Rest;
using Microsoft.EntityFrameworkCore;
using Sotd.Database;
using Attachment = Sotd.Database.Attachment;

namespace Sotd.Importer;

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
        await foreach (var page in channel.GetMessagesAsync(last, Direction.After, int.MaxValue))
        {
            foreach (var msg in page.OrderBy(m => m.Id))
            {
                if (msg.Id <= last) continue;
                last = msg.Id;
                if (msg.Type is not (MessageType.Default or MessageType.Reply)) continue;

                entries += await ProcessMessageAsync(msg, dbChannelId, channel.Name);
                messages++;
                if (messages % 50 == 0)
                    Console.WriteLine($"  #{channel.Name}: {messages} messages, {entries} entries (at {msg.Timestamp:yyyy-MM-dd})");
            }
        }
        Console.WriteLine($"#{channel.Name}: imported {messages} messages, {entries} entries");
    }

    /// <summary>Imports one message and everything attached to it in a single transaction. Returns the number of entries added.</summary>
    async Task<int> ProcessMessageAsync(IMessage msg, Guid channelId, string channelName)
    {
        using var db = NewDb();
        var discordId = (long)msg.Id;
        if (await db.Messages.AnyAsync(m => m.DiscordId == discordId)) return 0;

        var context = $"#{channelName} message {msg.Id}";
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

        var attachments = new List<(Attachment Row, IAttachment Source, string FullPath)>();
        foreach (var att in msg.Attachments)
        {
            var ext = Path.GetExtension(att.Filename).TrimStart('.').ToLowerInvariant();
            var fileExt = await GetOrCreateExtensionAsync(db, ext);

            var relative = Text.RelativePath("Download", "Attachments", $"{att.Id}-{Text.SanitizeFileName(att.Filename)}");
            var full = Path.Combine(_repoRoot, relative);
            if (!await _downloader.TryDownloadAsync(att.Url, full))
                throw new InvalidOperationException($"Could not download attachment {att.Filename} ({att.Id}) of {context}");

            var row = new Attachment
            {
                Id = Guid.NewGuid(),
                DiscordId = (long)att.Id,
                MessageId = message.Id,
                Filename = att.Filename,
                FileExtensionId = fileExt.Id,
                Size = att.Size,
                Path = relative,
            };
            db.Attachments.Add(row);
            attachments.Add((row, att, full));
        }
        await db.SaveChangesAsync();

        await AddReactionsAsync(db, msg, message);

        var writer = _writers.Create(db);
        var entryCount = 0;
        foreach (var link in Text.ExtractLinks(msg.Content))
            foreach (var media in await _resolver.ResolveLinkAsync(link, context))
            {
                await writer.AddEntryAsync(message, media, null, context);
                entryCount++;
            }

        foreach (var (row, source, full) in attachments.Where(a => IsAudioOrVideo(a.Source)))
        {
            var media = await _resolver.ResolveFileAsync(full, source.Filename, context);
            await writer.AddEntryAsync(message, media, row, context);
            entryCount++;
        }

        await tx.CommitAsync();
        return entryCount;
    }

    async Task AddReactionsAsync(SotdDbContext db, IMessage msg, Message message)
    {
        if (msg is not IUserMessage userMessage || userMessage.Reactions.Count == 0) return;

        var seen = new HashSet<(Guid Profile, Guid Emoji)>();
        foreach (var emote in userMessage.Reactions.Keys)
        {
            var emoji = await _emojis.EnsureAsync(db, emote);
            await foreach (var page in userMessage.GetReactionUsersAsync(emote, 1000))
                foreach (var user in page)
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
