using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sotd.Backend.Api;
using Sotd.Database;

namespace Sotd.Backend.Controllers;

public record TagDto(Guid Id, string Name);

/// <summary>One row of the annotation screen: the thing being annotated, plus its current tags in order.</summary>
public record AnnotationRow(Guid Id, string Label, string? Detail, IReadOnlyList<TagDto> Tags);

public class AddTagRequest
{
    public Guid OwnerId { get; set; }

    /// <summary>An existing tag picked from the dropdown.</summary>
    public Guid? TagId { get; set; }

    /// <summary>A brand new tag typed into the search box ("add X"). Creates the row before linking it.</summary>
    public string? NewName { get; set; }
}

public class ReorderRequest
{
    public Guid SongId { get; set; }

    /// <summary>The artist IDs in their new credited order; positions are renumbered from 1.</summary>
    public IReadOnlyList<Guid> ArtistIds { get; set; } = Array.Empty<Guid>();
}

/// <summary>
/// The three many-to-many relationships the spec allows editing: a song's artists, a song's genres, and a
/// profile's people. They differ only in which tables they join, so they share one set of endpoints.
/// </summary>
[ApiController]
[Route("api/annotations")]
public class AnnotationsController : ControllerBase
{
    public const string SongArtists = "song-artists";
    public const string SongGenres = "song-genres";
    public const string ProfilePersons = "profile-persons";

    readonly SotdDbContext _db;

    public AnnotationsController(SotdDbContext db) => _db = db;

    /// <summary>Which relationships can be edited, and how the UI should label them.</summary>
    [HttpGet("relationships")]
    public ActionResult<object> Relationships() => Ok(new[]
    {
        new { key = SongArtists, label = "Song to Artist", owner = "Song", tag = "Artist", ordered = true, big = true },
        new { key = SongGenres, label = "Song to Genre", owner = "Song", tag = "Genre", ordered = false, big = true },
        new { key = ProfilePersons, label = "Profile to Person", owner = "Profile", tag = "Person", ordered = false, big = false },
    });

    [HttpGet("{relationship}")]
    public async Task<ActionResult<PagedResult<AnnotationRow>>> List(string relationship, [FromQuery] ListQuery query)
    {
        switch (relationship)
        {
            case SongArtists:
            case SongGenres:
            {
                var songs = Query.ApplyFilter(_db.Songs.AsNoTracking(), SongColumns, query.Filter);
                var total = await songs.CountAsync();
                var (page, size) = Window(query, total);
                var rows = await Query
                    .ApplyOrder(songs, SongColumns, query.OrderBy, query.Desc, "title", false, (System.Linq.Expressions.LambdaExpression)SongColumns[0].Selector)
                    .Skip((page - 1) * size).Take(size).ToListAsync();

                var ids = rows.Select(s => s.Id).ToList();
                var tags = relationship == SongArtists
                    ? await _db.SongArtists.AsNoTracking().Where(sa => ids.Contains(sa.SongId))
                        .OrderBy(sa => sa.Position)
                        .Select(sa => new { Owner = sa.SongId, Tag = new TagDto(sa.ArtistId, sa.Artist.Name) })
                        .ToListAsync()
                    : await _db.SongGenres.AsNoTracking().Where(sg => ids.Contains(sg.SongId))
                        .OrderBy(sg => sg.Genre.Name)
                        .Select(sg => new { Owner = sg.SongId, Tag = new TagDto(sg.GenreId, sg.Genre.Name) })
                        .ToListAsync();

                var grouped = tags.GroupBy(t => t.Owner).ToDictionary(g => g.Key, g => (IReadOnlyList<TagDto>)g.Select(x => x.Tag).ToList());
                var items = rows.Select(s => new AnnotationRow(s.Id, s.Title, s.SpotifyId ?? s.Isrc,
                    grouped.TryGetValue(s.Id, out var t) ? t : Array.Empty<TagDto>())).ToList();
                return Ok(new PagedResult<AnnotationRow>(items, total, page, size));
            }
            case ProfilePersons:
            {
                // Profile is a small table, so every row is returned at once.
                var profiles = Query.ApplyFilter(_db.Profiles.AsNoTracking(), ProfileColumns, query.Filter);
                var rows = await Query
                    .ApplyOrder(profiles, ProfileColumns, query.OrderBy, query.Desc, "username", false, (System.Linq.Expressions.LambdaExpression)ProfileColumns[0].Selector)
                    .ToListAsync();
                var ids = rows.Select(p => p.Id).ToList();
                var tags = await _db.PersonProfiles.AsNoTracking().Where(pp => ids.Contains(pp.ProfileId))
                    .OrderBy(pp => pp.Person.Name)
                    .Select(pp => new { Owner = pp.ProfileId, Tag = new TagDto(pp.PersonId, pp.Person.Name) })
                    .ToListAsync();
                var grouped = tags.GroupBy(t => t.Owner).ToDictionary(g => g.Key, g => (IReadOnlyList<TagDto>)g.Select(x => x.Tag).ToList());
                var items = rows.Select(p => new AnnotationRow(p.Id, p.Username, p.DiscordId.ToString(),
                    grouped.TryGetValue(p.Id, out var t) ? t : Array.Empty<TagDto>())).ToList();
                return Ok(new PagedResult<AnnotationRow>(items, rows.Count, 1, rows.Count));
            }
            default:
                return NotFound(new ApiError($"'{relationship}' is not an editable relationship."));
        }
    }

    /// <summary>
    /// What to show in a row's search dropdown. With no term it returns everything, which is what the spec
    /// asks for when the box is first clicked.
    /// </summary>
    [HttpGet("{relationship}/options")]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> Options(string relationship, [FromQuery] string? q,
        [FromQuery] int limit = 200)
    {
        var term = q?.Trim();
        IQueryable<TagDto> options;
        switch (relationship)
        {
            case SongArtists:
            {
                var artists = _db.Artists.AsNoTracking();
                if (!string.IsNullOrEmpty(term)) artists = artists.Where(a => EF.Functions.Like(a.Name, "%" + term + "%"));
                options = artists.OrderBy(a => a.Name).Select(a => new TagDto(a.Id, a.Name));
                break;
            }
            case SongGenres:
            {
                var genres = _db.Genres.AsNoTracking();
                if (!string.IsNullOrEmpty(term)) genres = genres.Where(g => EF.Functions.Like(g.Name, "%" + term + "%"));
                options = genres.OrderBy(g => g.Name).Select(g => new TagDto(g.Id, g.Name));
                break;
            }
            case ProfilePersons:
            {
                var people = _db.People.AsNoTracking();
                if (!string.IsNullOrEmpty(term)) people = people.Where(p => EF.Functions.Like(p.Name, "%" + term + "%"));
                options = people.OrderBy(p => p.Name).Select(p => new TagDto(p.Id, p.Name));
                break;
            }
            default:
                return NotFound(new ApiError($"'{relationship}' is not an editable relationship."));
        }
        return Ok(await options.Take(Math.Clamp(limit, 1, 500)).ToListAsync());
    }

    /// <summary>Links an existing tag, or creates one from what was typed and links that.</summary>
    [HttpPost("{relationship}/tags")]
    public async Task<ActionResult<TagDto>> AddTag(string relationship, [FromBody] AddTagRequest request)
    {
        if (request.TagId == null && string.IsNullOrWhiteSpace(request.NewName))
            return BadRequest(new ApiError("Pick an existing tag or type a name to create one."));

        try
        {
            switch (relationship)
            {
                case SongArtists:
                {
                    if (!await _db.Songs.AnyAsync(s => s.Id == request.OwnerId))
                        return NotFound(new ApiError("That song no longer exists."));
                    var artist = await ResolveAsync(request,
                        id => _db.Artists.FirstOrDefaultAsync(a => a.Id == id),
                        name => _db.Artists.FirstOrDefaultAsync(a => a.Name == name),
                        // Artist has no other required column, so a tag can fill the whole row.
                        name => { var a = new Artist { Id = Guid.NewGuid(), Name = name }; _db.Artists.Add(a); return a; },
                        a => new TagDto(a.Id, a.Name));
                    if (artist.Error != null) return artist.Error;

                    if (await _db.SongArtists.AnyAsync(sa => sa.SongId == request.OwnerId && sa.ArtistId == artist.Tag!.Id))
                        return Conflict(new ApiError($"'{artist.Tag!.Name}' is already credited on this song."));

                    // New credits go on the end of the running order.
                    var next = await _db.SongArtists.Where(sa => sa.SongId == request.OwnerId)
                        .Select(sa => (int?)sa.Position).MaxAsync() ?? 0;
                    _db.SongArtists.Add(new SongArtist { SongId = request.OwnerId, ArtistId = artist.Tag!.Id, Position = next + 1 });
                    await _db.SaveChangesAsync();
                    return Ok(artist.Tag);
                }
                case SongGenres:
                {
                    if (!await _db.Songs.AnyAsync(s => s.Id == request.OwnerId))
                        return NotFound(new ApiError("That song no longer exists."));
                    var genre = await ResolveAsync(request,
                        id => _db.Genres.FirstOrDefaultAsync(g => g.Id == id),
                        name => _db.Genres.FirstOrDefaultAsync(g => g.Name == name),
                        // Genre's only other column is its parent, which is nullable, so leaving it null is fine.
                        name => { var g = new Genre { Id = Guid.NewGuid(), Name = name }; _db.Genres.Add(g); return g; },
                        g => new TagDto(g.Id, g.Name));
                    if (genre.Error != null) return genre.Error;

                    if (await _db.SongGenres.AnyAsync(sg => sg.SongId == request.OwnerId && sg.GenreId == genre.Tag!.Id))
                        return Conflict(new ApiError($"'{genre.Tag!.Name}' is already on this song."));
                    _db.SongGenres.Add(new SongGenre { SongId = request.OwnerId, GenreId = genre.Tag!.Id });
                    await _db.SaveChangesAsync();
                    return Ok(genre.Tag);
                }
                case ProfilePersons:
                {
                    if (!await _db.Profiles.AnyAsync(p => p.Id == request.OwnerId))
                        return NotFound(new ApiError("That profile no longer exists."));
                    var person = await ResolveAsync(request,
                        id => _db.People.FirstOrDefaultAsync(p => p.Id == id),
                        name => _db.People.FirstOrDefaultAsync(p => p.Name == name),
                        name => { var p = new Person { Id = Guid.NewGuid(), Name = name }; _db.People.Add(p); return p; },
                        p => new TagDto(p.Id, p.Name));
                    if (person.Error != null) return person.Error;

                    if (await _db.PersonProfiles.AnyAsync(pp => pp.ProfileId == request.OwnerId && pp.PersonId == person.Tag!.Id))
                        return Conflict(new ApiError($"'{person.Tag!.Name}' is already linked to this profile."));
                    _db.PersonProfiles.Add(new PersonProfile { ProfileId = request.OwnerId, PersonId = person.Tag!.Id });
                    await _db.SaveChangesAsync();
                    return Ok(person.Tag);
                }
                default:
                    return NotFound(new ApiError($"'{relationship}' is not an editable relationship."));
            }
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlite)
        {
            _db.ChangeTracker.Clear();
            return Conflict(new ApiError(Describe(sqlite), sqlite.Message));
        }
    }

    [HttpDelete("{relationship}/tags")]
    public async Task<ActionResult> RemoveTag(string relationship, [FromQuery] Guid ownerId, [FromQuery] Guid tagId)
    {
        try
        {
            switch (relationship)
            {
                case SongArtists:
                {
                    var link = await _db.SongArtists.FirstOrDefaultAsync(sa => sa.SongId == ownerId && sa.ArtistId == tagId);
                    if (link == null) return NotFound(new ApiError("That credit is already gone."));
                    _db.SongArtists.Remove(link);
                    await _db.SaveChangesAsync();
                    await RenumberAsync(ownerId);
                    return NoContent();
                }
                case SongGenres:
                {
                    var link = await _db.SongGenres.FirstOrDefaultAsync(sg => sg.SongId == ownerId && sg.GenreId == tagId);
                    if (link == null) return NotFound(new ApiError("That genre is already gone."));
                    _db.SongGenres.Remove(link);
                    break;
                }
                case ProfilePersons:
                {
                    var link = await _db.PersonProfiles.FirstOrDefaultAsync(pp => pp.ProfileId == ownerId && pp.PersonId == tagId);
                    if (link == null) return NotFound(new ApiError("That person is already gone."));
                    _db.PersonProfiles.Remove(link);
                    break;
                }
                default:
                    return NotFound(new ApiError($"'{relationship}' is not an editable relationship."));
            }
            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlite)
        {
            _db.ChangeTracker.Clear();
            return Conflict(new ApiError(Describe(sqlite), sqlite.Message));
        }
    }

    /// <summary>Renumbers a song's credits after the tags have been dragged into a new order.</summary>
    [HttpPut("song-artists/order")]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> Reorder([FromBody] ReorderRequest request)
    {
        var links = await _db.SongArtists.Include(sa => sa.Artist)
            .Where(sa => sa.SongId == request.SongId).ToListAsync();
        if (links.Count == 0) return NotFound(new ApiError("That song has no credits to reorder."));

        var requested = request.ArtistIds.Distinct().ToList();
        if (requested.Count != links.Count || requested.Any(id => links.All(l => l.ArtistId != id)))
            return BadRequest(new ApiError("The new order must list each of the song's artists exactly once."));

        for (var i = 0; i < requested.Count; i++)
            links.First(l => l.ArtistId == requested[i]).Position = i + 1;
        await _db.SaveChangesAsync();

        return Ok(links.OrderBy(l => l.Position).Select(l => new TagDto(l.ArtistId, l.Artist.Name)).ToList());
    }

    /// <summary>Closes the gaps in a song's credit positions so they stay 1, 2, 3, ... after a removal.</summary>
    async Task RenumberAsync(Guid songId)
    {
        var links = await _db.SongArtists.Where(sa => sa.SongId == songId).OrderBy(sa => sa.Position).ToListAsync();
        for (var i = 0; i < links.Count; i++) links[i].Position = i + 1;
        if (links.Count > 0) await _db.SaveChangesAsync();
    }

    record Resolved<T>(TagDto? Tag, ActionResult<TagDto>? Error);

    /// <summary>
    /// Finds the tag that was picked, or the one that was typed if it already exists, and only creates a new
    /// row when the name is genuinely new. Typing the exact name of an existing tag must not quietly make a
    /// second one, which would then sit next to the first in every dropdown.
    /// </summary>
    async Task<Resolved<T>> ResolveAsync<T>(AddTagRequest request, Func<Guid, Task<T?>> find,
        Func<string, Task<T?>> findByName, Func<string, T> create, Func<T, TagDto> toTag) where T : class
    {
        if (request.TagId is { } id)
        {
            var existing = await find(id);
            return existing == null
                ? new Resolved<T>(null, NotFound(new ApiError("That tag no longer exists.")))
                : new Resolved<T>(toTag(existing), null);
        }
        var name = request.NewName!.Trim();
        if (name.Length == 0) return new Resolved<T>(null, BadRequest(new ApiError("A new tag needs a name.")));
        if (await findByName(name) is { } named) return new Resolved<T>(toTag(named), null);

        var created = create(name);
        await _db.SaveChangesAsync();
        return new Resolved<T>(toTag(created), null);
    }

    static readonly IReadOnlyList<ColumnMap<Song>> SongColumns = new[]
    {
        Query.Col<Song, Guid>("id", s => s.Id),
        Query.Text<Song>("title", s => s.Title),
    };

    static readonly IReadOnlyList<ColumnMap<Profile>> ProfileColumns = new[]
    {
        Query.Col<Profile, Guid>("id", p => p.Id),
        Query.Text<Profile>("username", p => p.Username),
    };

    static (int Page, int Size) Window(ListQuery query, int total)
    {
        var size = query.PageSize is 10 or 20 or 50 ? query.PageSize : 20;
        var last = Math.Max(1, (int)Math.Ceiling(total / (double)size));
        return (Math.Clamp(query.Page, 1, last), size);
    }

    static string Describe(SqliteException ex) =>
        ex.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
            ? "That would break a foreign key: one of the rows involved no longer exists."
            : ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                ? "That name is already taken."
                : ex.Message;
}
