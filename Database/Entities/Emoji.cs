namespace Database.Entities;

public class Emoji
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public long? DiscordId { get; set; }
    public bool IsAnimated { get; set; }
    public string? Path { get; set; }
}
