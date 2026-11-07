namespace Database.Entities;

public class Profile
{
    public Guid Id { get; set; }
    public long DiscordId { get; set; }
    public string Username { get; set; } = "";
    public bool IsCurrentMember { get; set; }
    public string PicturePath { get; set; } = "";
}
