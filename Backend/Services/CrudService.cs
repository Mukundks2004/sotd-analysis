using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sotd.Backend.Api;
using Sotd.Database;

namespace Sotd.Backend.Services;

/// <summary>
/// The read/create/update/delete behaviour every non-join table shares. Each table only has to say which
/// columns it has, how it maps to and from its DTOs, and whether it is big enough to need paging; all of
/// that arrives as an <see cref="ITableDefinition{TEntity,TRead,TWrite}"/>.
/// </summary>
public class CrudService<TEntity, TRead, TWrite> : ICrudService<TEntity, TRead, TWrite>
    where TEntity : class
{
    readonly SotdDbContext _db;
    readonly ITableDefinition<TEntity, TRead, TWrite> _table;
    readonly IQueryBuilder _query;

    public CrudService(SotdDbContext db, ITableDefinition<TEntity, TRead, TWrite> table, IQueryBuilder query)
    {
        _db = db;
        _table = table;
        _query = query;
    }

    DbSet<TEntity> Set => _table.Set(_db);

    public async Task<ServiceResult<PagedResult<TRead>>> ListAsync(ListQuery query)
    {
        var columns = _table.Columns;
        var filtered = _query.ApplyFilter(_table.Include(Set.AsNoTracking()), columns, query.Filter);
        var total = await filtered.CountAsync();
        var ordered = _query.ApplyOrder(filtered, columns, query.OrderBy, query.Desc,
            _table.DefaultColumn, _table.DefaultDesc, _table.IdSelector);

        if (!_table.IsBig)
        {
            var all = await ordered.ToListAsync();
            return ServiceResult<PagedResult<TRead>>.Ok(
                new PagedResult<TRead>(all.Select(_table.ToRead).ToList(), all.Count, 1, all.Count));
        }

        var (page, size) = _query.Window(query, total);
        var items = await ordered.Skip((page - 1) * size).Take(size).ToListAsync();
        return ServiceResult<PagedResult<TRead>>.Ok(
            new PagedResult<TRead>(items.Select(_table.ToRead).ToList(), total, page, size));
    }

    public async Task<ServiceResult<TRead>> GetAsync(Guid id)
    {
        var entity = await FindAsync(id, track: false);
        return entity == null
            ? ServiceResult<TRead>.NotFound($"No row with ID {id}.")
            : ServiceResult<TRead>.Ok(_table.ToRead(entity));
    }

    public async Task<ServiceResult<TRead>> CreateAsync(TWrite write)
    {
        var entity = _table.NewEntity(write);
        Set.Add(entity);
        return await SaveAsync(() => _table.ToRead(entity));
    }

    public async Task<ServiceResult<TRead>> UpdateAsync(Guid id, TWrite write)
    {
        var entity = await FindAsync(id, track: true);
        if (entity == null) return ServiceResult<TRead>.NotFound($"No row with ID {id}.");
        _table.ApplyWrite(write, entity);
        return await SaveAsync(() => _table.ToRead(entity));
    }

    public async Task<ServiceResult<TRead>> DeleteAsync(IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0) return ServiceResult<TRead>.BadRequest("No rows were selected.");

        var entities = new List<TEntity>();
        foreach (var id in ids)
        {
            var entity = await FindAsync(id, track: true);
            if (entity == null) return ServiceResult<TRead>.NotFound($"No row with ID {id}. Nothing was deleted.");
            entities.Add(entity);
        }
        Set.RemoveRange(entities);
        return await SaveAsync(() => default!);
    }

    async Task<TEntity?> FindAsync(Guid id, bool track)
    {
        var source = track ? Set.AsQueryable() : Set.AsNoTracking();
        // Read the selector once: it is a property, so two reads give two lambdas with two different
        // parameter instances, and mixing them produces a predicate EF cannot translate.
        var selector = _table.IdSelector;
        var predicate = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(selector.Body, Expression.Constant(id)), selector.Parameters[0]);
        return await _table.Include(source).FirstOrDefaultAsync(predicate);
    }

    /// <summary>Saves, turning the database's own constraint failures into a message the UI can show.</summary>
    async Task<ServiceResult<TRead>> SaveAsync(Func<TRead> result)
    {
        try
        {
            await _db.SaveChangesAsync();
            return ServiceResult<TRead>.Ok(result());
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlite)
        {
            _db.ChangeTracker.Clear();
            return ServiceResult<TRead>.Conflict(SqliteErrors.Describe(sqlite), sqlite.Message);
        }
    }
}
