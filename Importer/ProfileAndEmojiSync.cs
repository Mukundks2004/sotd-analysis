using Database.Entities;
using Discord;
using Microsoft.EntityFrameworkCore;
using Sotd.Database;
using Emoji = Database.Entities.Emoji;

namespace Importer;

/// <summary>Creates profiles on first sight and refreshes username, membership and picture once per run.</summary>
public class ProfileSync : IProfileSync
{
    readonly IFileDownloader _downloader;
    readonly string _repoRoot;
    readonly HashSet<ulong> _memberIds = new();
    readonly HashSet<ulong> _updatedThisRun = new();

    public ProfileSync(IFileDownloader downloader, string repoRoot)
    {
        _downloader = downloader;
        _repoRoot = repoRoot;
    }

    /// <summary>The guild member list is only known once the run has started, so it is set rather than injected.</summary>
    public void SetMembers(IEnumerable<ulong> memberIds)
    {
        _memberIds.Clear();
        foreach (var id in memberIds) _memberIds.Add(id);
    }

    public async Task<Profile> EnsureAsync(SotdDbContext db, IUser user)
    {
        var discordId = (long)user.Id;
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.DiscordId == discordId);
        var isNew = profile == null;
        if (profile == null)
        {
            profile = new Profile { Id = Guid.NewGuid(), DiscordId = discordId };
            db.Profiles.Add(profile);
        }
        else if (_updatedThisRun.Contains(user.Id))
        {
            return profile;
        }

        var oldPath = profile.PicturePath;
        profile.Username = user.Username;
        profile.IsCurrentMember = _memberIds.Contains(user.Id);

        var url = user.GetAvatarUrl(ImageFormat.Auto, 256) ?? user.GetDefaultAvatarUrl();
        var relative = Text.RelativePath("Download", "Profiles",
            $"{user.Id}-{Text.SanitizeFileName(user.Username)}{FileDownloader.ExtensionFromUrl(url)}");
        if (await _downloader.TryDownloadAsync(url, Path.Combine(_repoRoot, relative)))
        {
            profile.PicturePath = relative;
            // Username or image type changed: remove the stale file.
            if (!string.IsNullOrEmpty(oldPath) && oldPath != relative)
            {
                var old = Path.Combine(_repoRoot, oldPath);
                if (File.Exists(old)) File.Delete(old);
            }
        }
        else
        {
            Console.WriteLine($"  warning: could not download profile picture for {user.Username} ({user.Id})");
            if (isNew) profile.PicturePath = "";
        }

        await db.SaveChangesAsync();
        _updatedThisRun.Add(user.Id);
        return profile;
    }

    /// <summary>Marks every profile in the db as a member or not, according to the guild member list.</summary>
    public async Task SyncMembershipAsync(SotdDbContext db)
    {
        foreach (var p in await db.Profiles.ToListAsync())
            p.IsCurrentMember = _memberIds.Contains((ulong)p.DiscordId);
        await db.SaveChangesAsync();
    }
}

public class EmojiSync : IEmojiSync
{
    readonly IFileDownloader _downloader;
    readonly string _repoRoot;

    public EmojiSync(IFileDownloader downloader, string repoRoot)
    {
        _downloader = downloader;
        _repoRoot = repoRoot;
    }

    /// <summary>Finds or creates the Emoji row for a reaction emote (custom emoji or default unicode emoji).</summary>
    public async Task<Emoji> EnsureAsync(SotdDbContext db, IEmote emote)
    {
        if (emote is Emote custom)
        {
            var discordId = (long)custom.Id;
            var existing = await db.Emojis.FirstOrDefaultAsync(e => e.DiscordId == discordId);
            if (existing != null) return existing;

            var relative = Text.RelativePath("Download", "Emojis",
                $"{custom.Id}-{Text.SanitizeFileName(custom.Name)}{FileDownloader.ExtensionFromUrl(custom.Url, custom.Animated ? ".gif" : ".png")}");
            var ok = await _downloader.TryDownloadAsync(custom.Url, Path.Combine(_repoRoot, relative));
            if (!ok) Console.WriteLine($"  warning: could not download emoji {custom.Name} ({custom.Id})");

            var emoji = new Emoji
            {
                Id = Guid.NewGuid(),
                Name = custom.Name,
                DiscordId = discordId,
                IsAnimated = custom.Animated,
                Path = ok ? relative : "",
            };
            db.Emojis.Add(emoji);
            await db.SaveChangesAsync();
            return emoji;
        }

        var name = emote.Name;
        var def = await db.Emojis.FirstOrDefaultAsync(e => e.DiscordId == null && e.Name == name);
        if (def != null) return def;
        def = new Emoji { Id = Guid.NewGuid(), Name = name, DiscordId = null, IsAnimated = false, Path = null };
        db.Emojis.Add(def);
        await db.SaveChangesAsync();
        return def;
    }
}
