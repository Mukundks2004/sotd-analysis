namespace Importer;

public record MediaArtist(string Name, string? SpotifyId);

public enum MatchQuality
{
    /// <summary>Song came straight from a Spotify link/playlist.</summary>
    SpotifyExact,
    /// <summary>Song came straight from the linked host's own API (Apple Music / Deezer): exact, not a guess.</summary>
    ProviderExact,
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
    /// <summary>The recording's ISRC, when the source gave one. Same recording, same ISRC, on every service.</summary>
    public string? Isrc { get; set; }
    public MatchQuality Quality { get; set; }

    public override string ToString() =>
        $"{Title} - {string.Join(", ", Artists.Select(a => a.Name))} [{SourceName}]";
}

/// <summary>What a row in a Spotify playlist actually is. Playlists are not only catalogue tracks.</summary>
public enum SpotifyItemKind
{
    /// <summary>A catalogue track.</summary>
    Track,
    /// <summary>A podcast episode. Has a Spotify id, but of an episode, and never an ISRC.</summary>
    Episode,
    /// <summary>
    /// A file from the playlist owner's own machine. Has no id, no ISRC and no public URL: the only
    /// information is the title/artist/album the owner's tags happened to carry.
    /// </summary>
    LocalFile,
}

/// <param name="Id">Null for a local file, which Spotify has no id for.</param>
public record SpotifyTrack(
    string? Id,
    string Name,
    List<MediaArtist> Artists,
    int DurationSeconds,
    DateTime? ReleaseDate,
    string? Isrc = null,
    SpotifyItemKind Kind = SpotifyItemKind.Track);

/// <summary>
/// A track as described by a non-Spotify music API (iTunes/Apple Music, Deezer). These give an exact
/// answer for their own links, so nothing has to be guessed from a page title.
/// </summary>
public record ProviderTrack(
    string Title,
    List<string> Artists,
    int? DurationSeconds,
    DateTime? ReleaseDate,
    string? Isrc = null);
