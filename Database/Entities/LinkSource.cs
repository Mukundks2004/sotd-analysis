namespace Database.Entities;

public class LinkSource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<SongLink> SongLinks { get; set; } = [];
}
