using Microsoft.EntityFrameworkCore;
using Sotd.Backend.Services;

namespace Backend.Tests;

/// <summary>The force-directed genre hierarchy: a forest, so the loop guard is the interesting part.</summary>
[TestFixture]
public class GenreGraphServiceTests : DatabaseTestBase
{
    GenreGraphService _service = null!;

    [SetUp]
    public void SetUpService() => _service = new GenreGraphService(Db);

    [Test]
    public async Task EveryGenreIsANodeAndEveryParentIsAnEdge()
    {
        var rock = AddGenre("Rock");
        var punk = AddGenre("Punk", rock.Id);
        AddGenre("Jazz");

        var graph = (await _service.GetAsync()).Value!;

        Assert.That(graph.Nodes.Select(n => n.Name), Is.EqualTo(new[] { "Jazz", "Punk", "Rock" }));
        Assert.That(graph.Edges.Single(), Is.EqualTo(new GenreEdge(rock.Id, punk.Id)));
    }

    [Test]
    public async Task ACreatedGenreStartsWithNoParent()
    {
        var result = await _service.CreateAsync("  Shoegaze  ");

        Assert.That(result.Value!.Name, Is.EqualTo("Shoegaze"));
        Assert.That((await _service.GetAsync()).Value!.Edges, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task AGenreNeedsAName(string? name) =>
        Assert.That((await _service.CreateAsync(name)).Status, Is.EqualTo(ResultStatus.BadRequest));

    [Test]
    public async Task TwoGenresCannotShareAName()
    {
        AddGenre("Rock");

        var result = await _service.CreateAsync("Rock");

        Assert.That(result.Status, Is.EqualTo(ResultStatus.Conflict));
        Assert.That(await Db.Genres.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task DrawingAnArrowSetsTheParentAndReturnsTheWholeGraph()
    {
        var rock = AddGenre("Rock");
        var punk = AddGenre("Punk");

        var graph = (await _service.SetParentAsync(punk.Id, rock.Id)).Value!;

        Assert.That(graph.Edges.Single(), Is.EqualTo(new GenreEdge(rock.Id, punk.Id)));
    }

    [Test]
    public async Task AGenreOnlyHasRoomForOneParentSoANewArrowReplacesTheOld()
    {
        var rock = AddGenre("Rock");
        var metal = AddGenre("Metal");
        var punk = AddGenre("Punk", rock.Id);

        var graph = (await _service.SetParentAsync(punk.Id, metal.Id)).Value!;

        Assert.That(graph.Edges.Single(), Is.EqualTo(new GenreEdge(metal.Id, punk.Id)));
    }

    [Test]
    public async Task AGenreCannotBeItsOwnParent()
    {
        var rock = AddGenre("Rock");

        var result = await _service.SetParentAsync(rock.Id, rock.Id);

        Assert.That(result.Status, Is.EqualTo(ResultStatus.BadRequest));
    }

    [Test]
    public async Task AnArrowThatWouldCloseALoopIsRefused()
    {
        // Rock -> Punk -> Hardcore; making Rock a child of Hardcore would close the cycle.
        var rock = AddGenre("Rock");
        var punk = AddGenre("Punk", rock.Id);
        var hardcore = AddGenre("Hardcore", punk.Id);

        var result = await _service.SetParentAsync(rock.Id, hardcore.Id);

        Assert.That(result.Status, Is.EqualTo(ResultStatus.Conflict));
        Assert.That(result.Message, Does.Contain("loop"));
        Assert.That(await Db.Genres.CountAsync(g => g.ParentId == hardcore.Id), Is.EqualTo(0));
    }

    [Test]
    public async Task AnArrowBetweenGenresThatAreGoneIsNotFound()
    {
        var rock = AddGenre("Rock");

        Assert.That((await _service.SetParentAsync(Guid.NewGuid(), rock.Id)).Status, Is.EqualTo(ResultStatus.NotFound));
        Assert.That((await _service.SetParentAsync(rock.Id, Guid.NewGuid())).Status, Is.EqualTo(ResultStatus.NotFound));
    }

    [Test]
    public async Task DeletingAnArrowMakesTheGenreARootAgain()
    {
        var rock = AddGenre("Rock");
        var punk = AddGenre("Punk", rock.Id);

        var graph = (await _service.RemoveParentAsync(punk.Id)).Value!;

        Assert.That(graph.Edges, Is.Empty);
        Assert.That(graph.Nodes, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task DetachingARootIsAlreadyDoneRatherThanAnError()
    {
        var rock = AddGenre("Rock");

        var result = await _service.RemoveParentAsync(rock.Id);

        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Value!.Edges, Is.Empty);
    }

    [Test]
    public async Task DetachingAGenreThatIsGoneIsNotFound() =>
        Assert.That((await _service.RemoveParentAsync(Guid.NewGuid())).Status, Is.EqualTo(ResultStatus.NotFound));
}
