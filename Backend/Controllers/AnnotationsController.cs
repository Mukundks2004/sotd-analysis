using Backend.Api;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>Routes for the annotation screen. All of its rules live in <see cref="IAnnotationService"/>.</summary>
[ApiController]
[Route("api/annotations")]
public class AnnotationsController : ControllerBase
{
    readonly IAnnotationService _annotations;

    public AnnotationsController(IAnnotationService annotations) => _annotations = annotations;

    [HttpGet("relationships")]
    public ActionResult<IReadOnlyList<RelationshipInfo>> Relationships() => Ok(_annotations.Relationships());

    [HttpGet("{relationship}")]
    public async Task<ActionResult<PagedResult<AnnotationRow>>> List(string relationship, [FromQuery] ListQuery query) =>
        (await _annotations.ListAsync(relationship, query)).ToActionResult();

    [HttpGet("{relationship}/options")]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> Options(string relationship, [FromQuery] string? q,
        [FromQuery] int limit = 200) =>
        (await _annotations.OptionsAsync(relationship, q, limit)).ToActionResult();

    [HttpPost("{relationship}/tags")]
    public async Task<ActionResult<TagDto>> AddTag(string relationship, [FromBody] AddTagRequest request) =>
        (await _annotations.AddTagAsync(relationship, request)).ToActionResult();

    [HttpDelete("{relationship}/tags")]
    public async Task<ActionResult<TagDto>> RemoveTag(string relationship, [FromQuery] Guid ownerId,
        [FromQuery] Guid tagId) =>
        (await _annotations.RemoveTagAsync(relationship, ownerId, tagId)).ToActionResult();

    [HttpPut("song-artists/order")]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> Reorder([FromBody] ReorderRequest request) =>
        (await _annotations.ReorderAsync(request)).ToActionResult();
}
