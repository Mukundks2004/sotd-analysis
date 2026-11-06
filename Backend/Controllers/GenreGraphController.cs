using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sotd.Backend.Api;
using Sotd.Database;

namespace Sotd.Backend.Controllers;

public record GenreNode(Guid Id, string Name);

/// <summary>A parent-to-child arrow. A genre has at most one parent, so a child appears at most once.</summary>
public record GenreEdge(Guid ParentId, Guid ChildId);

public record GenreGraph(IReadOnlyList<GenreNode> Nodes, IReadOnlyList<GenreEdge> Edges);

public class SetParentRequest
{
    public Guid ChildId { get; set; }
    public Guid ParentId { get; set; }
}

public class CreateGenreRequest
{
    public string Name { get; set; } = "";
}

/// <summary>Backs the force-directed genre hierarchy view.</summary>
[ApiController]
[Route("api/genre-graph")]
public class GenreGraphController : ControllerBase
{
    readonly SotdDbContext _db;

    public GenreGraphController(SotdDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<GenreGraph>> Get()
    {
        var genres = await _db.Genres.AsNoTracking().OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.Name, g.ParentId }).ToListAsync();
        return Ok(new GenreGraph(
            genres.Select(g => new GenreNode(g.Id, g.Name)).ToList(),
            genres.Where(g => g.ParentId != null).Select(g => new GenreEdge(g.ParentId!.Value, g.Id)).ToList()));
    }

    [HttpPost]
    public async Task<ActionResult<GenreNode>> Create([FromBody] CreateGenreRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0) return BadRequest(new ApiError("A genre needs a name."));
        if (await _db.Genres.AnyAsync(g => g.Name == name))
            return Conflict(new ApiError($"There is already a genre called '{name}'."));

        var genre = new Genre { Id = Guid.NewGuid(), Name = name };
        _db.Genres.Add(genre);
        await _db.SaveChangesAsync();
        return Ok(new GenreNode(genre.Id, genre.Name));
    }

    /// <summary>
    /// Drawing an arrow makes the first genre the parent of the second. A genre only has room for one
    /// parent, so an arrow into a genre that already has one replaces it.
    /// </summary>
    [HttpPut("edges")]
    public async Task<ActionResult<GenreGraph>> SetParent([FromBody] SetParentRequest request)
    {
        if (request.ParentId == request.ChildId)
            return BadRequest(new ApiError("A genre cannot be its own parent."));

        var child = await _db.Genres.FirstOrDefaultAsync(g => g.Id == request.ChildId);
        if (child == null) return NotFound(new ApiError("That genre no longer exists."));
        if (!await _db.Genres.AnyAsync(g => g.Id == request.ParentId))
            return NotFound(new ApiError("That parent genre no longer exists."));

        // Walking up from the proposed parent must not lead back to the child, or the tree becomes a loop
        // that nothing could ever draw or query.
        var parents = await _db.Genres.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.ParentId);
        var cursor = request.ParentId;
        var guard = parents.Count + 1;
        while (guard-- > 0)
        {
            if (cursor == request.ChildId)
                return Conflict(new ApiError("That would make a loop: the genre you picked is already below this one."));
            if (!parents.TryGetValue(cursor, out var next) || next == null) break;
            cursor = next.Value;
        }

        child.ParentId = request.ParentId;
        await _db.SaveChangesAsync();
        return await Get();
    }

    /// <summary>Detaches a genre from its parent, making it a root again.</summary>
    [HttpDelete("edges")]
    public async Task<ActionResult<GenreGraph>> RemoveParent([FromQuery] Guid childId)
    {
        var child = await _db.Genres.FirstOrDefaultAsync(g => g.Id == childId);
        if (child == null) return NotFound(new ApiError("That genre no longer exists."));
        if (child.ParentId == null) return await Get();

        child.ParentId = null;
        await _db.SaveChangesAsync();
        return await Get();
    }
}
