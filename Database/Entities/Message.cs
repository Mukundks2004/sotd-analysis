namespace Database.Entities;

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
