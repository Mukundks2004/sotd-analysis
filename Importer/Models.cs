namespace Sotd.Importer;

public record MediaArtist(string Name, string? SpotifyId);

public enum MatchQuality
{
    /// <summary>Song came straight from a Spotify link/playlist.</summary>
    SpotifyExact,
    /// <summary>Song was found by a Spotify string search (a guess, logged for review).</summary>
    SpotifySearch,
    /// <summary>No Spotify match: raw title and uploader (logged for review).</summary>
    Raw,
}

/// <summary>Everything the importer worked out about one song of the day, before touching the db.</summary>
public class ResolvedMedia
{
    public string SourceName { get; set; } = "";
    public string? Link { get; set; }
    public string Title { get; set; } = "";
    public List<MediaArtist> Artists { get; set; } = new();
    /// <summary>Seconds of the linked media. 0 if none could be found.</summary>
    public int DurationSeconds { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? SpotifyTrackId { get; set; }
    public MatchQuality Quality { get; set; }

    public override string ToString() =>
        $"{Title} - {string.Join(", ", Artists.Select(a => a.Name))} [{SourceName}]";
}

public record SpotifyTrack(string Id, string Name, List<MediaArtist> Artists, int DurationSeconds, DateTime? ReleaseDate);
