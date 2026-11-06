using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Sotd.Backend.Api;

/// <summary>Everything the list endpoints accept, bound from the query string.</summary>
public class ListQuery
{
    /// <summary>Column to order by. Falls back to the table's default column from Database/spec.md.</summary>
    public string? OrderBy { get; set; }

    /// <summary>True for descending. Falls back to the table's default direction.</summary>
    public bool? Desc { get; set; }

    /// <summary>Free text matched against every text column of the table.</summary>
    public string? Filter { get; set; }

    /// <summary>1-based. Ignored by the small tables, which always return everything.</summary>
    public int Page { get; set; } = 1;

    /// <summary>10, 20 or 50 in the UI. Ignored by the small tables.</summary>
    public int PageSize { get; set; } = 20;
}

/// <summary>One page of rows, plus what the UI needs to render the pager.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

/// <summary>The shape of every failure response, so the UI can always show a message in its toast.</summary>
public record ApiError(string Message, string? Detail = null);

/// <summary>
/// Describes one sortable/filterable column of a table. The backend keeps an explicit list per table rather
/// than reflecting over the entity, so a request can never order by or search something that is not a real
/// column, and columns the UI must not search (the profile picture) can simply be left out.
/// </summary>
public record ColumnMap<TEntity>(string Name, LambdaExpression Selector, bool IsText);

/// <summary>Builds the ordering, filtering and paging that every list endpoint shares.</summary>
public static class Query
{
    /// <summary>Declares a sortable, non-text column.</summary>
    public static ColumnMap<T> Col<T, TKey>(string name, Expression<Func<T, TKey>> selector) =>
        new(name, selector, false);

    /// <summary>Declares a column that is also searched by the filter bar.</summary>
    public static ColumnMap<T> Text<T>(string name, Expression<Func<T, string?>> selector) =>
        new(name, selector, true);

    /// <summary>
    /// Orders by the requested column, always breaking ties on ID so that paging is stable and two rows
    /// sharing a name never swap places between pages.
    /// </summary>
    public static IQueryable<TEntity> ApplyOrder<TEntity>(
        IQueryable<TEntity> source,
        IReadOnlyList<ColumnMap<TEntity>> columns,
        string? requested,
        bool? desc,
        string defaultColumn,
        bool defaultDesc,
        LambdaExpression? tieBreak)
    {
        var name = requested is { Length: > 0 } ? requested : defaultColumn;
        var column = columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                     ?? columns.FirstOrDefault(c => string.Equals(c.Name, defaultColumn, StringComparison.OrdinalIgnoreCase));
        if (column == null) return source;

        // Only the default column carries a default direction; any other column starts ascending.
        var descending = desc ?? (string.Equals(column.Name, defaultColumn, StringComparison.OrdinalIgnoreCase) && defaultDesc);
        var ordered = Order(source, column.Selector, descending, first: true);
        return tieBreak == null ? ordered : Order(ordered, tieBreak, descending: false, first: false);
    }

    static IOrderedQueryable<TEntity> Order<TEntity>(IQueryable<TEntity> source, LambdaExpression selector,
        bool descending, bool first)
    {
        var method = (first, descending) switch
        {
            (true, false) => nameof(Queryable.OrderBy),
            (true, true) => nameof(Queryable.OrderByDescending),
            (false, false) => nameof(Queryable.ThenBy),
            _ => nameof(Queryable.ThenByDescending),
        };
        // Selectors declared as Func<T, object?> make the compiler insert a box that EF cannot translate,
        // so the Convert is peeled off and the real key type is used instead.
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } u ? u.Operand : selector.Body;
        var lambda = Expression.Lambda(body, selector.Parameters);
        var call = typeof(Queryable).GetMethods()
            .First(m => m.Name == method && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TEntity), body.Type);
        return (IOrderedQueryable<TEntity>)call.Invoke(null, new object[] { source, lambda })!;
    }

    /// <summary>Keeps rows where the term appears in any of the table's own text columns.</summary>
    public static IQueryable<TEntity> ApplyFilter<TEntity>(
        IQueryable<TEntity> source, IReadOnlyList<ColumnMap<TEntity>> columns, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return source;
        var term = filter.Trim();
        var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var like = typeof(DbFunctionsExtensions).GetMethod(nameof(DbFunctionsExtensions.Like),
            new[] { typeof(DbFunctions), typeof(string), typeof(string), typeof(string) })!;
        var functions = Expression.Property(null, typeof(EF), nameof(EF.Functions));

        Expression? predicate = null;
        foreach (var column in columns.Where(c => c.IsText))
        {
            var body = new ReplaceParameter(column.Selector.Parameters[0], parameter).Visit(column.Selector.Body)!;
            var call = Expression.Call(like, functions, body, Expression.Constant(pattern), Expression.Constant("\\"));
            predicate = predicate == null ? call : Expression.OrElse(predicate, call);
        }
        return predicate == null ? source : source.Where(Expression.Lambda<Func<TEntity, bool>>(predicate, parameter));
    }

    class ReplaceParameter : ExpressionVisitor
    {
        readonly ParameterExpression _from, _to;
        public ReplaceParameter(ParameterExpression from, ParameterExpression to) => (_from, _to) = (from, to);
        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : node;
    }
}
