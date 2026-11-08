using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Sotd.Database;

namespace Importer;

/// <summary>In-memory index of song ids by match-normalized title, so name matching does not scan the Song table.</summary>
public class SongTitleIndex : ISongTitleIndex
{
    readonly Dictionary<string, List<Guid>> _byTitle = new();

    public async Task LoadAsync(SotdDbContext db)
    {
        _byTitle.Clear();
        foreach (var s in await db.Songs.AsNoTracking().Select(s => new { s.Id, s.Title }).ToListAsync())
            Add(s.Title, s.Id);
    }

    public void Add(string title, Guid id)
    {
        var key = Text.NormalizeForMatching(title);
        if (!_byTitle.TryGetValue(key, out var list)) _byTitle[key] = list = new();
        list.Add(id);
    }

    public IReadOnlyList<Guid> Find(string title) =>
        _byTitle.TryGetValue(Text.NormalizeForMatching(title), out var list) ? list : Array.Empty<Guid>();
}

/// <summary>Hands out a <see cref="CatalogWriter"/> bound to the context the caller is using for one message.</summary>
public class CatalogWriterFactory : ICatalogWriterFactory
{
    readonly ISongTitleIndex _songs;
    readonly ISpotifyClient _spotify;
    readonly IReviewLog _log;

    public CatalogWriterFactory(ISongTitleIndex songs, ISpotifyClient spotify, IReviewLog log)
    {
        _songs = songs;
        _spotify = spotify;
        _log = log;
    }

    public ICatalogWriter Create(SotdDbContext db) => new CatalogWriter(db, _songs, _spotify, _log);
}

/// <summary>
/// Writes resolved songs to the db, matching existing artists and songs first: by ISRC, then Spotify ID,
/// then by name (artist) or match-normalized title plus ordered artists (song).
/// </summary>
public class CatalogWriter : ICatalogWriter
{
    readonly SotdDbContext _db;
    readonly ISongTitleIndex _songs;
    readonly ISpotifyClient _spotify;
    readonly IReviewLog _log;

    public CatalogWriter(SotdDbContext db, ISongTitleIndex songs, ISpotifyClient spotify, IReviewLog log)
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

        if (media.Quality is not (MatchQuality.SpotifyExact or MatchQuality.ProviderExact))
            _log.NameMatched(context, $"{media.Quality}: {media} {media.Link}");

        var entry = new SotdEntry { Id = Guid.NewGuid(), MessageId = message.Id };
        if (attachment != null)
        {
            var attachmentSong = new AttachmentSong { AttachmentId = attachment.Id, SongId = song.Id };
            _db.AttachmentSongs.Add(attachmentSong);
            entry.AttachmentSongId = attachmentSong.AttachmentId;
        }
        else
        {
            var link = await GetOrCreateSongLinkAsync(song, media);
            entry.SongLinkId = link.Id;
        }
        _db.SotdEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    async Task<SongLink> GetOrCreateSongLinkAsync(Song song, ResolvedMedia media)
    {
        var url = media.Link ?? throw new InvalidOperationException($"Link media without a url: {media}");
        var link = await _db.SongLinks.FirstOrDefaultAsync(l => l.Url == url);
        if (link != null) return link;

        var source = await GetOrCreateLinkSourceAsync(media.SourceName);
        link = new SongLink
        {
            Id = Guid.NewGuid(),
            Url = url,
            SongId = song.Id,
            LinkSourceId = source.Id,
            Duration = media.DurationSeconds,
            UploadDate = media.ReleaseDate,
        };
        _db.SongLinks.Add(link);
        return link;
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
        // ISRC first: one recording keeps the same ISRC on every service, while its Spotify id can differ
        // between markets and album releases.
        if (media.Isrc != null)
            song = await _db.Songs.FirstOrDefaultAsync(s => s.Isrc == media.Isrc);
        if (song == null && media.SpotifyTrackId != null)
            song = await _db.Songs.FirstOrDefaultAsync(s => s.SpotifyId == media.SpotifyTrackId);

        if (song == null)
        {
            foreach (var id in _songs.Find(media.Title))
            {
                var candidate = await _db.Songs.FirstAsync(s => s.Id == id);
                // Two different recordings are two songs, even with the same title and artists.
                if (media.Isrc != null && candidate.Isrc != null && candidate.Isrc != media.Isrc) continue;
                if (media.SpotifyTrackId != null && candidate.SpotifyId != null &&
                    candidate.SpotifyId != media.SpotifyTrackId) continue;

                var existing = await _db.SongArtists.Where(x => x.SongId == id)
                    .OrderBy(x => x.Position).Select(x => x.ArtistId).ToListAsync();
                if (!existing.SequenceEqual(artistIds)) continue;

                song = candidate;
                break;
            }
        }

        if (song != null)
        {
            await AdoptIdentifiersAsync(song, media);
            return song;
        }

        song = new Song { Id = Guid.NewGuid(), Title = media.Title, SpotifyId = media.SpotifyTrackId, Isrc = media.Isrc };
        _db.Songs.Add(song);
        for (var i = 0; i < artistIds.Count; i++)
            _db.SongArtists.Add(new SongArtist { SongId = song.Id, ArtistId = artistIds[i], Position = i + 1 });
        await _db.SaveChangesAsync();
        _songs.Add(song.Title, song.Id);
        return song;
    }

    /// <summary>
    /// A song first seen without Spotify/ISRC information picks them up the next time it is posted from a
    /// source that knows them. Guarded so a value already used by another row is never duplicated.
    /// </summary>
    async Task AdoptIdentifiersAsync(Song song, ResolvedMedia media)
    {
        var changed = false;
        if (media.SpotifyTrackId != null && song.SpotifyId == null &&
            !await _db.Songs.AnyAsync(s => s.SpotifyId == media.SpotifyTrackId))
        {
            song.SpotifyId = media.SpotifyTrackId;
            changed = true;
        }
        if (media.Isrc != null && song.Isrc == null && !await _db.Songs.AnyAsync(s => s.Isrc == media.Isrc))
        {
            song.Isrc = media.Isrc;
            changed = true;
        }
        if (changed) await _db.SaveChangesAsync();
    }

    async Task<LinkSource> GetOrCreateLinkSourceAsync(string name)
    {
        var source = await _db.LinkSources.FirstOrDefaultAsync(s => s.Name == name);
        if (source != null) return source;
        source = new LinkSource { Id = Guid.NewGuid(), Name = name };
        _db.LinkSources.Add(source);
        await _db.SaveChangesAsync();
        return source;
    }
}
