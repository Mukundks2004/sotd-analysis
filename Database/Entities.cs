namespace Sotd.Database;

// Entities mirror Database/spec.md. Discord snowflakes are stored as long (signed 64-bit INTEGER).







public class Channel
{
    public Guid Id { get; set; }
    public long DiscordId { get; set; }
    public string Name { get; set; } = "";
}

public class Message
{
    public Guid Id { get; set; }
    public long DiscordId { get; set; }
    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;
    public Guid PostedById { get; set; }
    public Profile PostedBy { get; set; } = null!;
    public DateTime PostedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public string Content { get; set; } = "";
    public Guid? ReplyId { get; set; }
    public Message? Reply { get; set; }
}

public class FileExtension
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class Attachment
{
    public Guid Id { get; set; }
    public long DiscordId { get; set; }
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public string Filename { get; set; } = "";
    public Guid FileExtensionId { get; set; }
    public FileExtension FileExtension { get; set; } = null!;
    public long Size { get; set; }
    public string Path { get; set; } = "";
}

public class Emoji
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public long? DiscordId { get; set; }
    public bool IsAnimated { get; set; }
    public string? Path { get; set; }
}

public class Reaction
{
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public Guid EmojiId { get; set; }
    public Emoji Emoji { get; set; } = null!;
}

public class Song
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? SpotifyId { get; set; }
    /// <summary>
    /// International Standard Recording Code. The same recording has the same ISRC on Spotify, Apple Music,
    /// Deezer and so on, so this is what ties one song together across sources.
    /// </summary>
    public string? Isrc { get; set; }
    public List<SongArtist> SongArtists { get; set; } = new();
}

public class Genre
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public Genre? Parent { get; set; }
}

public class SongGenre
{
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public Guid GenreId { get; set; }
    public Genre Genre { get; set; } = null!;
}

public class Artist
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? SpotifyId { get; set; }
}

public class SongArtist
{
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public Guid ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public int Position { get; set; } = 1;
}

public class ArtistGenre
{
    public Guid ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public Guid GenreId { get; set; }
    public Genre Genre { get; set; } = null!;
}

public class Source
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class SotdEntry
{
    public Guid Id { get; set; }
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public Guid SourceId { get; set; }
    public Source Source { get; set; } = null!;
    public string? Link { get; set; }
    public Guid? AttachmentId { get; set; }
    public Attachment? Attachment { get; set; }
    /// <summary>Seconds. 0 means the importer could not find a duration (logged for review).</summary>
    public int Duration { get; set; }
    public DateTime? ReleaseDate { get; set; }
}
