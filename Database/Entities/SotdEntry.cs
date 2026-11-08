namespace Database.Entities;

public class SotdEntry
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public Guid? AttachmentId { get; set; }
    public Attachment? Attachment { get; set; }
}
