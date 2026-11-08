namespace Database.Entities;

public class AttachmentSong
{
    // Doubles as the primary key: one song row per attachment.
    public Guid AttachmentId { get; set; }
    public Attachment Attachment { get; set; } = null!;
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public ICollection<SotdEntry> SotdEntries { get; set; } = new List<SotdEntry>();
}
