using Database.Entities;
using Discord;
using Sotd.Database;
using Attachment = Database.Entities.Attachment;
using Emoji = Database.Entities.Emoji;

namespace Importer;

// Every service the importer is built from sits behind an interface, so each class can be tested on its own
// with the things it talks to replaced by mocks. The implementations are the only place that touches the
// network, the file system, a child process or the database.

/// <summary>Spotify Web API: the catalogue the importer treats as the source of truth for song identity.</summary>
public interface ISpotifyClient
{
    Task<SpotifyTrack?> GetTrackAsync(string id);

    /// <summary>A podcast episode by id, for a link that points straight at one.</summary>
    Task<SpotifyTrack?> GetEpisodeAsync(string id);

    /// <summary>Exact lookup of a recording by ISRC: not a guess, unlike a string search.</summary>
    Task<SpotifyTrack?> FindByIsrcAsync(string isrc);

    /// <summary>Tracks, podcast episodes and local files, in playlist order.</summary>
    Task<List<SpotifyTrack>> GetPlaylistTracksAsync(string id);

    Task<List<SpotifyTrack>> SearchTracksAsync(string query);

    Task<List<string>> GetArtistGenresAsync(string artistId);
}

/// <summary>Apple Music metadata through the public iTunes lookup API.</summary>
public interface IITunesClient
{
    Task<ProviderTrack?> GetTrackAsync(string trackId);
}

/// <summary>Deezer metadata through its public API. The one free source of ISRCs.</summary>
public interface IDeezerClient
{
    Task<ProviderTrack?> GetTrackAsync(string trackId);
    Task<List<ProviderTrack>> GetPlaylistTracksAsync(string playlistId);
}

/// <summary>Last-resort metadata scraped from a page's JSON-LD or Open Graph tags.</summary>
public interface IPageMetadataClient
{
    Task<PageMetadata?> GetAsync(string url, string? preferTrackId = null);
}

/// <summary>yt-dlp, for metadata only. Never downloads media.</summary>
public interface IYtDlpClient
{
    /// <summary>Throws if yt-dlp cannot be started. Call once at startup.</summary>
    Task EnsureAvailableAsync();

    /// <summary>One item for a single video, or one per entry for a playlist. Empty when the link cannot be read.</summary>
    Task<List<YtDlpItem>> GetAsync(string url);
}

/// <summary>Downloads profile pictures, emojis and attachments.</summary>
public interface IFileDownloader
{
    Task<bool> TryDownloadAsync(string url, string destination);
}

/// <summary>Reads duration and tags out of an uploaded audio/video file.</summary>
public interface IFileMetadataReader
{
    FileMetadata? Read(string path);
}

/// <summary>Decides what a posted URL is before anything tries to resolve it.</summary>
public interface ILinkClassifier
{
    LinkInfo? Classify(string url);
}

/// <summary>Plain-text logs of everything a person should check by hand after a run.</summary>
public interface IReviewLog
{
    void SkippedLink(string context, string url);
    void Unresolvable(string context, string url, string reason);
    void ZeroDuration(string context, string description);
    void NameMatched(string context, string description);
}

/// <summary>Turns links and uploaded files into the song they refer to.</summary>
public interface IMediaResolver
{
    Task<List<ResolvedMedia>> ResolveLinkAsync(string url, string context);
    Task<ResolvedMedia> ResolveFileAsync(string localPath, string filename, string context);
}

/// <summary>Song ids by match-normalized title, so name matching does not scan the Song table.</summary>
public interface ISongTitleIndex
{
    void Add(string title, Guid id);
    IReadOnlyList<Guid> Find(string title);

    /// <summary>Fills the index from the database. Call once per run.</summary>
    Task LoadAsync(SotdDbContext db);
}

/// <summary>Writes resolved songs and their entries to the database.</summary>
public interface ICatalogWriter
{
    Task<SotdEntry> AddEntryAsync(Message message, ResolvedMedia media, Attachment? attachment, string context);
}

/// <summary>
/// A writer is bound to one <see cref="SotdDbContext"/>, and the importer opens a fresh context per message,
/// so writers are made on demand rather than injected once.
/// </summary>
public interface ICatalogWriterFactory
{
    ICatalogWriter Create(SotdDbContext db);
}

/// <summary>Opens a new database context. One per message, so a failed message rolls back on its own.</summary>
public interface ISotdDbContextFactory
{
    SotdDbContext Create();
}

/// <summary>Creates profiles on first sight and refreshes username, membership and picture once per run.</summary>
public interface IProfileSync
{
    /// <summary>The guild's current member list, which decides <c>IsCurrentMember</c>. Set once per run.</summary>
    void SetMembers(IEnumerable<ulong> memberIds);

    Task<Profile> EnsureAsync(SotdDbContext db, IUser user);

    /// <summary>Marks every profile in the db as a member or not, according to the guild member list.</summary>
    Task SyncMembershipAsync(SotdDbContext db);
}

/// <summary>Finds or creates the Emoji row for a reaction emote.</summary>
public interface IEmojiSync
{
    Task<Emoji> EnsureAsync(SotdDbContext db, IEmote emote);
}

/// <summary>The <c>--import</c> run: read the configured channels and write what they contain. No music services.</summary>
public interface IDiscordImporter
{
    Task RunAsync();
}

/// <summary>The <c>--process</c> run: turn already-imported messages into songs, links and SOTD entries.</summary>
public interface IMessageProcessor
{
    Task RunAsync();
}
