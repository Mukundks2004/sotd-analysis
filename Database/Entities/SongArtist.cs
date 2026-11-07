namespace Database.Entities;

public class SongArtist
{
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public Guid ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public int Position { get; set; } = 1;
}
