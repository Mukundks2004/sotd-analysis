using Database;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Backs the force-directed genre hierarchy view.</summary>
public class GenreGraphService : IGenreGraphService
{
    readonly SotdDbContext _db;

    public GenreGraphService(SotdDbContext db) => _db = db;

    public async Task<ServiceResult<GenreGraph>> GetAsync()
    {
        var genres = await _db.Genres.AsNoTracking().OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.Name, g.ParentId }).ToListAsync();
        return ServiceResult<GenreGraph>.Ok(new GenreGraph(
            genres.Select(g => new GenreNode(g.Id, g.Name)).ToList(),
            genres.Where(g => g.ParentId != null).Select(g => new GenreEdge(g.ParentId!.Value, g.Id)).ToList()));
    }

    public async Task<ServiceResult<GenreNode>> CreateAsync(string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0) return ServiceResult<GenreNode>.BadRequest("A genre needs a name.");
        if (await _db.Genres.AnyAsync(g => g.Name == name))
            return ServiceResult<GenreNode>.Conflict($"There is already a genre called '{name}'.");

        var genre = new Genre { Id = Guid.NewGuid(), Name = name };
        _db.Genres.Add(genre);
        await _db.SaveChangesAsync();
        return ServiceResult<GenreNode>.Ok(new GenreNode(genre.Id, genre.Name));
    }

    /// <summary>
    /// Drawing an arrow makes the first genre the parent of the second. A genre only has room for one
    /// parent, so an arrow into a genre that already has one replaces it.
    /// </summary>
    public async Task<ServiceResult<GenreGraph>> SetParentAsync(Guid childId, Guid parentId)
    {
        if (parentId == childId)
            return ServiceResult<GenreGraph>.BadRequest("A genre cannot be its own parent.");

        var child = await _db.Genres.FirstOrDefaultAsync(g => g.Id == childId);
        if (child == null) return ServiceResult<GenreGraph>.NotFound("That genre no longer exists.");
        if (!await _db.Genres.AnyAsync(g => g.Id == parentId))
            return ServiceResult<GenreGraph>.NotFound("That parent genre no longer exists.");

        // Walking up from the proposed parent must not lead back to the child, or the tree becomes a loop
        // that nothing could ever draw or query.
        var parents = await _db.Genres.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.ParentId);
        var cursor = parentId;
        var guard = parents.Count + 1;
        while (guard-- > 0)
        {
            if (cursor == childId)
                return ServiceResult<GenreGraph>.Conflict("That would make a loop: the genre you picked is already below this one.");
            if (!parents.TryGetValue(cursor, out var next) || next == null) break;
            cursor = next.Value;
        }

        child.ParentId = parentId;
        await _db.SaveChangesAsync();
        return await GetAsync();
    }

    /// <summary>Detaches a genre from its parent, making it a root again.</summary>
    public async Task<ServiceResult<GenreGraph>> RemoveParentAsync(Guid childId)
    {
        var child = await _db.Genres.FirstOrDefaultAsync(g => g.Id == childId);
        if (child == null) return ServiceResult<GenreGraph>.NotFound("That genre no longer exists.");
        if (child.ParentId == null) return await GetAsync();

        child.ParentId = null;
        await _db.SaveChangesAsync();
        return await GetAsync();
    }
}
