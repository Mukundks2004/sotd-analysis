namespace Database.Entities;

public class Channel
{
    public Guid Id { get; set; }
    public long DiscordId { get; set; }
    public string Name { get; set; } = "";
}
