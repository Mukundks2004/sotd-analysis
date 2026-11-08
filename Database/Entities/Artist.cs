namespace Database.Entities;

public class Artist
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? SpotifyId { get; set; }
    public string? AppleId { get; set; }
    public string? YoutubeChannelId { get; set; }
    public ICollection<SongArtist> SongArtists { get; set; } = new List<SongArtist>();
    public ICollection<ArtistGenre> ArtistGenres { get; set; } = new List<ArtistGenre>();
}
