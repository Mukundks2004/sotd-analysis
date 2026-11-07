namespace Database.Entities;

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
