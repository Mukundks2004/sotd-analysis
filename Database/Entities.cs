namespace Sotd.Database;

// Entities mirror Database/spec.md. Discord snowflakes are stored as long (signed 64-bit INTEGER).

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
