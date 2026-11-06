using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sotd.Database;

namespace Sotd.Backend.Api;

/// <summary>
/// The read/create/update/delete behaviour every non-join table shares. Each table only has to say which
/// columns it has, how it maps to and from its DTOs, and whether it is big enough to need paging.
/// </summary>
[ApiController]
public abstract class CrudController<TEntity, TRead, TWrite> : ControllerBase
    where TEntity : class
{
    protected readonly SotdDbContext Db;

    protected CrudController(SotdDbContext db) => Db = db;

    protected abstract DbSet<TEntity> Set { get; }

    /// <summary>Every column the UI can order by, with the text ones marked for the filter bar.</summary>
    protected abstract IReadOnlyList<ColumnMap<TEntity>> Columns { get; }

    /// <summary>The primary key, used for lookups and as the tie-break of every ordering.</summary>
    protected abstract Expression<Func<TEntity, Guid>> IdSelector { get; }

    /// <summary>The default ordering from Database/spec.md.</summary>
    protected abstract string DefaultColumn { get; }

    protected virtual bool DefaultDesc => false;

    /// <summary>Big tables page; small tables always return every row.</summary>
    protected abstract bool IsBig { get; }

    protected abstract TRead ToRead(TEntity entity);

    protected abstract TEntity NewEntity(TWrite write);

    protected abstract void ApplyWrite(TWrite write, TEntity entity);

    /// <summary>Rows the table should always pull in to build its DTOs (for example a row's related names).</summary>
    protected virtual IQueryable<TEntity> Include(IQueryable<TEntity> source) => source;

    [HttpGet]
    public async Task<ActionResult<PagedResult<TRead>>> List([FromQuery] ListQuery query)
    {
        var filtered = Query.ApplyFilter(Include(Set.AsNoTracking()), Columns, query.Filter);
        var total = await filtered.CountAsync();
        var ordered = Query.ApplyOrder(filtered, Columns, query.OrderBy, query.Desc, DefaultColumn, DefaultDesc, IdSelector);

        if (!IsBig)
        {
            var all = await ordered.ToListAsync();
            return Ok(new PagedResult<TRead>(all.Select(ToRead).ToList(), all.Count, 1, all.Count));
        }

        var size = query.PageSize is 10 or 20 or 50 ? query.PageSize : 20;
        var page = Math.Max(1, query.Page);
        // Asking for a page past the end after a filter is narrowed should show the last page, not nothing.
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)size));
        if (page > lastPage) page = lastPage;

        var items = await ordered.Skip((page - 1) * size).Take(size).ToListAsync();
        return Ok(new PagedResult<TRead>(items.Select(ToRead).ToList(), total, page, size));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TRead>> Get(Guid id)
    {
        var entity = await Find(id, track: false);
        return entity == null ? NotFound(new ApiError($"No row with ID {id}.")) : Ok(ToRead(entity));
    }

    [HttpPost]
    public async Task<ActionResult<TRead>> Create([FromBody] TWrite write)
    {
        var entity = NewEntity(write);
        Set.Add(entity);
        return await SaveAsync(() => ToRead(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TRead>> Update(Guid id, [FromBody] TWrite write)
    {
        var entity = await Find(id, track: true);
        if (entity == null) return NotFound(new ApiError($"No row with ID {id}."));
        ApplyWrite(write, entity);
        return await SaveAsync(() => ToRead(entity));
    }

    [HttpDelete("{id:guid}")]
    public Task<ActionResult<TRead>> Delete(Guid id) => DeleteMany(new DeleteRequest { Ids = new[] { id } });

    /// <summary>
    /// Deleting is all or nothing: the whole selection is removed in one transaction, so a single row that
    /// something else still points at leaves the database exactly as it was.
    /// </summary>
    [HttpPost("delete")]
    public async Task<ActionResult<TRead>> DeleteMany([FromBody] DeleteRequest request)
    {
        if (request.Ids.Count == 0) return BadRequest(new ApiError("No rows were selected."));

        var entities = new List<TEntity>();
        foreach (var id in request.Ids)
        {
            var entity = await Find(id, track: true);
            if (entity == null) return NotFound(new ApiError($"No row with ID {id}. Nothing was deleted."));
            entities.Add(entity);
        }
        Set.RemoveRange(entities);
        return await SaveAsync(() => default!);
    }

    async Task<TEntity?> Find(Guid id, bool track)
    {
        var source = track ? Set.AsQueryable() : Set.AsNoTracking();
        // Read the selector once: it is a property, so two reads give two lambdas with two different
        // parameter instances, and mixing them produces a predicate EF cannot translate.
        var selector = IdSelector;
        var predicate = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(selector.Body, Expression.Constant(id)), selector.Parameters[0]);
        return await Include(source).FirstOrDefaultAsync(predicate);
    }

    /// <summary>Saves, turning the database's own constraint failures into a message the UI can show.</summary>
    protected async Task<ActionResult<TRead>> SaveAsync(Func<TRead> result)
    {
        try
        {
            await Db.SaveChangesAsync();
            return Ok(result());
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlite)
        {
            Db.ChangeTracker.Clear();
            return Conflict(new ApiError(Describe(sqlite), sqlite.Message));
        }
    }

    /// <summary>Turns SQLite's terse constraint text into something a person can act on.</summary>
    protected static string Describe(SqliteException ex)
    {
        var message = ex.Message;
        if (message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
            return "That would break a foreign key: one of the IDs does not point at an existing row, or another table still refers to this row.";
        if (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            return $"That value is already taken: {Between(message, "constraint failed: ") ?? "a unique column"} must be unique.";
        if (message.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase))
            return $"A required value is missing: {Between(message, "constraint failed: ") ?? "a column"} cannot be empty.";
        return message;
    }

    static string? Between(string text, string after)
    {
        var index = text.IndexOf(after, StringComparison.Ordinal);
        if (index < 0) return null;
        var rest = text[(index + after.Length)..].Trim();
        var end = rest.IndexOfAny(new[] { '\r', '\n', '\'' });
        return (end < 0 ? rest : rest[..end]).TrimEnd('.', ' ');
    }
}

public class DeleteRequest
{
    public IReadOnlyList<Guid> Ids { get; set; } = Array.Empty<Guid>();
}
