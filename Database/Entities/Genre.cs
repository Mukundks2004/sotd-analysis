namespace Database.Entities;

public class Genre
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public Genre? Parent { get; set; }
    public ICollection<Genre> Children { get; set; } = [];
    public ICollection<SongGenre> SongGenres { get; set; } = [];
    public ICollection<ArtistGenre> ArtistGenres { get; set; } = [];
}
