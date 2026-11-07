namespace Database.Entities;

public class Reaction
{
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public Guid EmojiId { get; set; }
    public Emoji Emoji { get; set; } = null!;
}
