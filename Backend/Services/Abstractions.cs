using System.Linq.Expressions;
using Backend.Api;
using Database;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Builds the ordering, filtering and paging that every list endpoint shares.</summary>
public interface IQueryBuilder
{
    /// <summary>Keeps rows where the term appears in any of the table's own text columns.</summary>
    IQueryable<TEntity> ApplyFilter<TEntity>(IQueryable<TEntity> source, IReadOnlyList<ColumnMap<TEntity>> columns, string? filter);

    /// <summary>
    /// Orders by the requested column, always breaking ties on ID so that paging is stable and two rows
    /// sharing a name never swap places between pages.
    /// </summary>
    IQueryable<TEntity> ApplyOrder<TEntity>(IQueryable<TEntity> source, IReadOnlyList<ColumnMap<TEntity>> columns,
        string? requested, bool? desc, string defaultColumn, bool defaultDesc, LambdaExpression? tieBreak);

    /// <summary>Clamps the requested page and page size to what the UI offers and what the data allows.</summary>
    (int Page, int Size) Window(ListQuery query, int total);
}

/// <summary>
/// Everything that makes one table different from another: its set, its columns, its default ordering and
/// how it maps to and from its DTOs. The CRUD service supplies all the behaviour around it.
/// </summary>
public interface ITableDefinition<TEntity, TRead, TWrite> where TEntity : class
{
    DbSet<TEntity> Set(SotdDbContext db);

    /// <summary>Every column the UI can order by, with the text ones marked for the filter bar.</summary>
    IReadOnlyList<ColumnMap<TEntity>> Columns { get; }

    /// <summary>The primary key, used for lookups and as the tie-break of every ordering.</summary>
    Expression<Func<TEntity, Guid>> IdSelector { get; }

    /// <summary>The default ordering from Database/spec.md.</summary>
    string DefaultColumn { get; }

    bool DefaultDesc { get; }

    /// <summary>Big tables page; small tables always return every row.</summary>
    bool IsBig { get; }

    TRead ToRead(TEntity entity);
    TEntity NewEntity(TWrite write);
    void ApplyWrite(TWrite write, TEntity entity);

    /// <summary>Rows the table should always pull in to build its DTOs (for example a row's related names).</summary>
    IQueryable<TEntity> Include(IQueryable<TEntity> source);
}

/// <summary>The read/create/update/delete behaviour every non-join table shares.</summary>
public interface ICrudService<TEntity, TRead, TWrite> where TEntity : class
{
    Task<ServiceResult<PagedResult<TRead>>> ListAsync(ListQuery query);
    Task<ServiceResult<TRead>> GetAsync(Guid id);
    Task<ServiceResult<TRead>> CreateAsync(TWrite write);
    Task<ServiceResult<TRead>> UpdateAsync(Guid id, TWrite write);

    /// <summary>
    /// Deleting is all or nothing: the whole selection is removed in one transaction, so a single row that
    /// something else still points at leaves the database exactly as it was.
    /// </summary>
    Task<ServiceResult<TRead>> DeleteAsync(IReadOnlyList<Guid> ids);
}

/// <summary>The three many-to-many relationships the spec allows editing.</summary>
public interface IAnnotationService
{
    IReadOnlyList<RelationshipInfo> Relationships();
    Task<ServiceResult<PagedResult<AnnotationRow>>> ListAsync(string relationship, ListQuery query);
    Task<ServiceResult<IReadOnlyList<TagDto>>> OptionsAsync(string relationship, string? term, int limit);
    Task<ServiceResult<TagDto>> AddTagAsync(string relationship, AddTagRequest request);
    Task<ServiceResult<TagDto>> RemoveTagAsync(string relationship, Guid ownerId, Guid tagId);
    Task<ServiceResult<IReadOnlyList<TagDto>>> ReorderAsync(ReorderRequest request);
}

/// <summary>Backs the force-directed genre hierarchy view.</summary>
public interface IGenreGraphService
{
    Task<ServiceResult<GenreGraph>> GetAsync();
    Task<ServiceResult<GenreNode>> CreateAsync(string? name);
    Task<ServiceResult<GenreGraph>> SetParentAsync(Guid childId, Guid parentId);
    Task<ServiceResult<GenreGraph>> RemoveParentAsync(Guid childId);
}
