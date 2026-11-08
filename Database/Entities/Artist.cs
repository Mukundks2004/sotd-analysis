namespace Database.Entities;

public class Artist
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? SpotifyId { get; set; }
    public string? AppleId { get; set; }
    public string? YoutubeChannelId { get; set; }
}
