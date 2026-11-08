namespace Database.Entities;

public class Song
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? SpotifyId { get; set; }
    public string? AppleId { get; set; }
    public string? YoutubeId { get; set; }
    public string? Isrc { get; set; }
    public ICollection<SongLink> SongLinks { get; set; } = [];
    public ICollection<SongArtist> SongArtists { get; set; } = [];
    public ICollection<SongGenre> SongGenres { get; set; } = [];
    public ICollection<AttachmentSong> AttachmentSongs { get; set; } = [];
}
