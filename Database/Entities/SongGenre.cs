namespace Database.Entities;

public class SongGenre
{
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public Guid GenreId { get; set; }
    public Genre Genre { get; set; } = null!;
}
