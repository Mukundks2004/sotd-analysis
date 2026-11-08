namespace Database.Entities;

public class Profile
{
    public Guid Id { get; set; }
    public long DiscordId { get; set; }
    public string Username { get; set; } = "";
    public bool IsCurrentMember { get; set; }
    public string PicturePath { get; set; } = "";
    public ICollection<PersonProfile> PersonProfiles { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<Reaction> Reactions { get; set; } = [];
}
