namespace Database.Entities;

public class ArtistGenre
{
    public Guid ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public Guid GenreId { get; set; }
    public Genre Genre { get; set; } = null!;
}
