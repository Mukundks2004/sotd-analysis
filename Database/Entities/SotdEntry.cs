namespace Database.Entities;

public class SotdEntry
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public Guid? SongLinkId { get; set; }
    public SongLink? SongLink { get; set; }
    public Guid? AttachmentSongId { get; set; }
    public AttachmentSong? AttachmentSong { get; set; }
}
