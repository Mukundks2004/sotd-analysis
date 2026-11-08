namespace Database.Entities;

public class SongLink
{
    public Guid Id { get; set; }
    public string Url { get; set; } = "";
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public Guid LinkSourceId { get; set; }
    public LinkSource LinkSource { get; set; } = null!;
    public int Duration { get; set; }
    public DateTime? UploadDate { get; set; }
    public string? Path { get; set; }
    public string? Lyrics { get; set; }
    public ICollection<SotdEntry> SotdEntries { get; set; } = [];
}
