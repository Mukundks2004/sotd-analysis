using System.Linq.Expressions;
using Backend.Api;

namespace Backend.Services;

/// <summary>
/// The instance face of the shared query helpers. The expression work itself stays in <see cref="Query"/>
/// so the table definitions can keep declaring their columns with it.
/// </summary>
public class QueryBuilder : IQueryBuilder
{
    public IQueryable<TEntity> ApplyFilter<TEntity>(IQueryable<TEntity> source,
        IReadOnlyList<ColumnMap<TEntity>> columns, string? filter) =>
        Query.ApplyFilter(source, columns, filter);

    public IQueryable<TEntity> ApplyOrder<TEntity>(IQueryable<TEntity> source,
        IReadOnlyList<ColumnMap<TEntity>> columns, string? requested, bool? desc, string defaultColumn,
        bool defaultDesc, LambdaExpression? tieBreak) =>
        Query.ApplyOrder(source, columns, requested, desc, defaultColumn, defaultDesc, tieBreak);

    public (int Page, int Size) Window(ListQuery query, int total)
    {
        // The UI only ever offers these three sizes, so anything else is someone poking at the URL.
        var size = query.PageSize is 10 or 20 or 50 ? query.PageSize : 20;
        // Asking for a page past the end after a filter is narrowed should show the last page, not nothing.
        var last = Math.Max(1, (int)Math.Ceiling(total / (double)size));
        return (Math.Clamp(query.Page, 1, last), size);
    }
}
