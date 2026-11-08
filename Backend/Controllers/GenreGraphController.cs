using Backend.Api;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>Routes for the force-directed genre hierarchy view.</summary>
[ApiController]
[Route("api/genre-graph")]
public class GenreGraphController : ControllerBase
{
    readonly IGenreGraphService _graph;

    public GenreGraphController(IGenreGraphService graph) => _graph = graph;

    [HttpGet]
    public async Task<ActionResult<GenreGraph>> Get() => (await _graph.GetAsync()).ToActionResult();

    [HttpPost]
    public async Task<ActionResult<GenreNode>> Create([FromBody] CreateGenreRequest request) =>
        (await _graph.CreateAsync(request.Name)).ToActionResult();

    [HttpPut("edges")]
    public async Task<ActionResult<GenreGraph>> SetParent([FromBody] SetParentRequest request) =>
        (await _graph.SetParentAsync(request.ChildId, request.ParentId)).ToActionResult();

    [HttpDelete("edges")]
    public async Task<ActionResult<GenreGraph>> RemoveParent([FromQuery] Guid childId) =>
        (await _graph.RemoveParentAsync(childId)).ToActionResult();
}
