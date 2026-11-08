using Backend.Api;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Sotd.Backend.Services;
using Sotd.Database;

namespace Backend.Tests;

/// <summary>
/// The ordering, filtering and paging every list endpoint shares. These run against real sqlite because
/// the whole point of the expression work is that EF can translate it.
/// </summary>
[TestFixture]
public class QueryBuilderTests : DatabaseTestBase
{
    readonly IQueryBuilder _query = new QueryBuilder();

    static readonly IReadOnlyList<ColumnMap<Person>> PersonColumns = new[]
    {
        Query.Col<Person, Guid>("id", p => p.Id),
        Query.Text<Person>("name", p => p.Name),
    };

    [Test]
    public void OrderingAlwaysBreaksTiesOnIdSoPagingIsStable()
    {
        // Two rows with the same name must not be able to swap places between two requests.
        var a = AddPerson("same");
        var b = AddPerson("same");
        AddPerson("zzz last");

        var first = Order(null, null).Select(p => p.Id).ToList();
        var second = Order("name", false).Select(p => p.Id).ToList();

        Assert.That(first.Take(2), Is.EquivalentTo(new[] { a.Id, b.Id }));
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void PagingThroughTiedRowsSeesEachOneExactlyOnce()
    {
        // Without the ID tie-break SQLite is free to return tied rows in a different order per page,
        // which shows some rows twice and hides others entirely.
        for (var i = 0; i < 5; i++) AddPerson("same");

        var seen = new List<Guid>();
        for (var page = 0; page < 3; page++)
            seen.AddRange(_query.ApplyOrder(Db.People.AsNoTracking(), PersonColumns, null, null, "name", false,
                    (System.Linq.Expressions.Expression<Func<Person, Guid>>)(p => p.Id))
                .Skip(page * 2).Take(2).Select(p => p.Id));

        Assert.That(seen, Is.Unique);
        Assert.That(seen, Has.Count.EqualTo(5));
    }

    [Test]
    public void AnUnknownColumnFallsBackToTheTablesDefault()
    {
        AddPerson("beta");
        AddPerson("alpha");

        Assert.That(Order("; drop table People", null).Select(p => p.Name),
            Is.EqualTo(new[] { "alpha", "beta" }));
    }

    [Test]
    public void OnlyTheDefaultColumnCarriesADefaultDirection()
    {
        AddPerson("alpha");
        AddPerson("beta");

        // "name" is the default column here, so defaultDesc applies to it...
        Assert.That(Order("name", null, defaultDesc: true).Select(p => p.Name),
            Is.EqualTo(new[] { "beta", "alpha" }));
        // ...but a different column starts ascending regardless.
        Assert.That(Order("id", null, defaultDesc: true).Select(p => p.Id),
            Is.Ordered.Ascending);
    }

    [Test]
    public void FilteringMatchesAnyTextColumnCaseInsensitively()
    {
        AddPerson("Alice");
        AddPerson("Bob");

        Assert.That(Filter("ali").Select(p => p.Name), Is.EqualTo(new[] { "Alice" }));
    }

    [Test]
    public void WildcardsTypedIntoTheFilterAreLiteralText()
    {
        // Someone searching for "100%" must not match everything.
        AddPerson("100% pure");
        AddPerson("anything");

        Assert.That(Filter("100%").Select(p => p.Name), Is.EqualTo(new[] { "100% pure" }));
        Assert.That(Filter("_"), Is.Empty);
    }

    [Test]
    public void AnEmptyFilterIsNotAFilter()
    {
        AddPerson("alpha");
        Assert.That(Filter("   ").Count(), Is.EqualTo(1));
        Assert.That(Filter(null).Count(), Is.EqualTo(1));
    }

    [TestCase(10, ExpectedResult = 10)]
    [TestCase(20, ExpectedResult = 20)]
    [TestCase(50, ExpectedResult = 50)]
    [TestCase(0, ExpectedResult = 20)]
    [TestCase(1000, ExpectedResult = 20)]
    [TestCase(-5, ExpectedResult = 20)]
    public int OnlyThePageSizesTheUiOffersAreAccepted(int requested) =>
        _query.Window(new ListQuery { PageSize = requested }, total: 500).Size;

    [Test]
    public void APageBeyondTheEndShowsTheLastPageRatherThanNothing()
    {
        // Narrowing the filter while sitting on page 9 should land on the last page of the new result.
        Assert.That(_query.Window(new ListQuery { Page = 9, PageSize = 10 }, total: 25).Page, Is.EqualTo(3));
    }

    [Test]
    public void AnEmptyTableStillHasAFirstPage()
    {
        Assert.That(_query.Window(new ListQuery { Page = 1, PageSize = 10 }, total: 0).Page, Is.EqualTo(1));
        Assert.That(_query.Window(new ListQuery { Page = 0, PageSize = 10 }, total: 5).Page, Is.EqualTo(1));
    }

    List<Person> Order(string? requested, bool? desc, bool defaultDesc = false) =>
        _query.ApplyOrder(Db.People.AsNoTracking(), PersonColumns, requested, desc, "name", defaultDesc,
            (System.Linq.Expressions.Expression<Func<Person, Guid>>)(p => p.Id)).ToList();

    List<Person> Filter(string? filter) =>
        _query.ApplyFilter(Db.People.AsNoTracking(), PersonColumns, filter).ToList();
}
