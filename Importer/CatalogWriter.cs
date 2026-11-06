using Microsoft.EntityFrameworkCore;
using Sotd.Database;

namespace Sotd.Importer;

/// <summary>In-memory index of song ids by normalized title, so name matching does not scan the Song table.</summary>
public class SongTitleIndex
{
    readonly Dictionary<string, List<Guid>> _byTitle = new();

    public static async Task<SongTitleIndex> LoadAsync(SotdDbContext db)
    {
        var index = new SongTitleIndex();
        foreach (var s in await db.Songs.AsNoTracking().Select(s => new { s.Id, s.Title }).ToListAsync())
            index.Add(s.Title, s.Id);
        return index;
    }

    public void Add(string title, Guid id)
    {
        var key = Text.NormalizeTitle(title);
        if (!_byTitle.TryGetValue(key, out var list)) _byTitle[key] = list = new();
        list.Add(id);
    }

    public IReadOnlyList<Guid> Find(string title) =>
        _byTitle.TryGetValue(Text.NormalizeTitle(title), out var list) ? list : Array.Empty<Guid>();
}

/// <summary>
/// Writes resolved songs to the db, matching existing artists and songs first:
/// by Spotify ID, then by name (artist) or normalized title plus ordered artists (song).
/// </summary>
public class CatalogWriter
{
    readonly SotdDbContext _db;
    readonly SongTitleIndex _songs;
    readonly SpotifyClient _spotify;
    readonly ReviewLog _log;

    public CatalogWriter(SotdDbContext db, SongTitleIndex songs, SpotifyClient spotify, ReviewLog log)
    {
        _db = db;
        _songs = songs;
        _spotify = spotify;
        _log = log;
    }

    /// <summary>Adds a SOTD entry for <paramref name="message"/> (and any new song, artists, genres and source it needs).</summary>
    public async Task<SotdEntry> AddEntryAsync(Message message, ResolvedMedia media, Attachment? attachment, string context)
    {
        var artistIds = new List<Guid>();
        foreach (var a in media.Artists)
        {
            var id = await GetOrCreateArtistAsync(a, context);
            if (!artistIds.Contains(id)) artistIds.Add(id);
        }

        var song = await GetOrCreateSongAsync(media, artistIds, context);
        var source = await GetOrCreateSourceAsync(media.SourceName);

        if (media.Quality != MatchQuality.SpotifyExact)
            _log.NameMatched(context, $"{media.Quality}: {media} {media.Link}");

        var entry = new SotdEntry
        {
            Id = Guid.NewGuid(),
            SongId = song.Id,
            MessageId = message.Id,
            SourceId = source.Id,
            Link = media.Link,
            AttachmentId = attachment?.Id,
            Duration = media.DurationSeconds,
            ReleaseDate = media.ReleaseDate,
        };
        _db.SotdEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    async Task<Guid> GetOrCreateArtistAsync(MediaArtist a, string context)
    {
        Artist? artist = null;
        if (a.SpotifyId != null)
        {
            artist = await _db.Artists.FirstOrDefaultAsync(x => x.SpotifyId == a.SpotifyId);
            if (artist == null)
            {
                // An artist first seen by name only (e.g. from a YouTube uploader) becomes the Spotify one.
                artist = await _db.Artists.FirstOrDefaultAsync(x => x.Name == a.Name && x.SpotifyId == null);
                if (artist != null)
                {
                    artist.SpotifyId = a.SpotifyId;
                    await AddGenresAsync(artist, context);
                    await _db.SaveChangesAsync();
                    return artist.Id;
                }
            }
        }
        else
        {
            artist = await _db.Artists.FirstOrDefaultAsync(x => x.Name == a.Name);
        }
        if (artist != null) return artist.Id;

        artist = new Artist { Id = Guid.NewGuid(), Name = a.Name, SpotifyId = a.SpotifyId };
        _db.Artists.Add(artist);
        await AddGenresAsync(artist, context);
        await _db.SaveChangesAsync(); // save now so a later artist/genre in the same entry can find these rows
        return artist.Id;
    }

    async Task AddGenresAsync(Artist artist, string context)
    {
        if (artist.SpotifyId == null) return;
        List<string> names;
        try
        {
            names = await _spotify.GetArtistGenresAsync(artist.SpotifyId);
        }
        catch (HttpRequestException ex)
        {
            _log.NameMatched(context, $"could not fetch genres for artist {artist.Name}: {ex.Message}");
            return;
        }
        foreach (var name in names.Distinct())
        {
            var genre = await _db.Genres.FirstOrDefaultAsync(g => g.Name == name)
                        ?? _db.Genres.Local.FirstOrDefault(g => g.Name == name);
            if (genre == null)
            {
                genre = new Genre { Id = Guid.NewGuid(), Name = name };
                _db.Genres.Add(genre);
            }
            _db.ArtistGenres.Add(new ArtistGenre { ArtistId = artist.Id, GenreId = genre.Id });
        }
    }

    async Task<Song> GetOrCreateSongAsync(ResolvedMedia media, List<Guid> artistIds, string context)
    {
        Song? song = null;
        if (media.SpotifyTrackId != null)
            song = await _db.Songs.FirstOrDefaultAsync(s => s.SpotifyId == media.SpotifyTrackId);

        if (song == null)
        {
            foreach (var id in _songs.Find(media.Title))
            {
                var candidate = await _db.Songs.FirstAsync(s => s.Id == id);
                // Two different Spotify tracks are two songs, even with the same title and artists.
                if (media.SpotifyTrackId != null && candidate.SpotifyId != null) continue;

                var existing = await _db.SongArtists.Where(x => x.SongId == id)
                    .OrderBy(x => x.Position).Select(x => x.ArtistId).ToListAsync();
                if (!existing.SequenceEqual(artistIds)) continue;

                song = candidate;
                if (media.SpotifyTrackId != null && candidate.SpotifyId == null)
                {
                    // Same song first seen without Spotify info: it now gets its Spotify ID.
                    candidate.SpotifyId = media.SpotifyTrackId;
                    await _db.SaveChangesAsync();
                }
                break;
            }
        }
        if (song != null) return song;

        song = new Song { Id = Guid.NewGuid(), Title = media.Title, SpotifyId = media.SpotifyTrackId };
        _db.Songs.Add(song);
        for (var i = 0; i < artistIds.Count; i++)
            _db.SongArtists.Add(new SongArtist { SongId = song.Id, ArtistId = artistIds[i], Position = i + 1 });
        await _db.SaveChangesAsync();
        _songs.Add(song.Title, song.Id);
        return song;
    }

    async Task<Source> GetOrCreateSourceAsync(string name)
    {
        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Name == name);
        if (source != null) return source;
        source = new Source { Id = Guid.NewGuid(), Name = name };
        _db.Sources.Add(source);
        await _db.SaveChangesAsync();
        return source;
    }
}
