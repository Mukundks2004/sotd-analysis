using Sotd.Database;

namespace Database.Entities;

public class Song
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? SpotifyId { get; set; }
    public string? Isrc { get; set; }
    public string? Path { get; set; }
    public string? Lyrics { get; set; }
    public List<SongArtist> SongArtists { get; set; } = [];
}
