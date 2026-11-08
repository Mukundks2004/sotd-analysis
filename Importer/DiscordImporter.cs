using Database.Entities;
using Discord;
using Discord.Rest;
using Microsoft.EntityFrameworkCore;
using Sotd.Database;
using Attachment = Database.Entities.Attachment;
using Channel = Database.Entities.Channel;

namespace Importer;

/// <summary>An attachment that has been downloaded to disk, ready to be written to the database.</summary>
public sealed record PreparedAttachment(IAttachment Source, string RelativePath, string Extension);

/// <summary>One emote on a message together with everyone who reacted with it.</summary>
public sealed record PreparedReaction(IEmote Emote, List<IUser> Users);

/// <summary>
/// A message with all of its slow network work already done, ready to be written to the database.
/// Preparing these in parallel is what makes the importer fast; they are still written one at a time
/// in message order so ordering and resume behave exactly as they did serially.
/// </summary>
public sealed record PreparedMessage(
    IMessage Message,
    string Context,
    List<PreparedAttachment> Attachments,
    List<PreparedReaction> Reactions);

/// <summary>
/// The <c>--import</c> run: copies profiles, channels, messages, attachments, emojis and reactions out of Discord.
/// It never calls Spotify, Apple Music or any other music service; <see cref="MessageProcessor"/> does that.
/// </summary>
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
    readonly IProfileSync _profiles;
    readonly IEmojiSync _emojis;

    public DiscordImporter(ImporterConfig config, string repoRoot, ISotdDbContextFactory dbs,
        IFileDownloader downloader, IProfileSync profiles, IEmojiSync emojis)
    {
        _config = config;
        _repoRoot = repoRoot;
        _dbs = dbs;
        _downloader = downloader;
        _profiles = profiles;
        _emojis = emojis;
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
                row = new Channel { Id = Guid.NewGuid(), DiscordId = did, Name = channel.Name };
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

        int messages = 0;
        var last = (ulong)lastStored;

        // Walking the channel is cheap; downloading attachments and listing reactors is not. So the messages
        // stream out in order here and OrderedPipeline does that work many at a time, while still writing in order.
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
                await WriteMessageAsync(prepared, dbChannelId);
                messages++;
                // The first one is worth announcing: on a fresh run it is the proof that writing has started.
                if (messages == 1 || messages % 50 == 0)
                    Console.WriteLine($"  #{channel.Name}: {messages} messages (at {prepared.Message.Timestamp:yyyy-MM-dd})");
            });

        Console.WriteLine($"#{channel.Name}: imported {messages} messages");
    }

    /// <summary>
    /// Everything about a message that needs the network: downloading its attachments and listing who reacted.
    /// Touches no database state, so many of these can run at once.
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

            var ext = Path.GetExtension(att.Filename).TrimStart('.').ToLowerInvariant();
            attachments.Add(new PreparedAttachment(att, relative, ext));
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

        return new PreparedMessage(msg, context, attachments, reactions);
    }

    /// <summary>Writes one prepared message and everything attached to it in a single transaction.</summary>
    async Task WriteMessageAsync(PreparedMessage prepared, Guid channelId)
    {
        var msg = prepared.Message;
        using var db = NewDb();
        var discordId = (long)msg.Id;
        if (await db.Messages.AnyAsync(m => m.DiscordId == discordId)) return;

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

        foreach (var att in prepared.Attachments)
        {
            var fileExt = await GetOrCreateExtensionAsync(db, att.Extension);
            db.Attachments.Add(new Attachment
            {
                Id = Guid.NewGuid(),
                DiscordId = (long)att.Source.Id,
                MessageId = message.Id,
                Filename = att.Source.Filename,
                FileExtensionId = fileExt.Id,
                Size = att.Source.Size,
                Path = att.RelativePath,
            });
        }
        await db.SaveChangesAsync();

        await AddReactionsAsync(db, prepared, message);

        await tx.CommitAsync();
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
        return IsAudioOrVideoFilename(a.Filename);
    }

    /// <summary>Extension-only check, for attachments already stored (the content type is not kept).</summary>
    public static bool IsAudioOrVideoFilename(string filename) =>
        AudioVideoExtensions.Contains(Path.GetExtension(filename).TrimStart('.'));
}
