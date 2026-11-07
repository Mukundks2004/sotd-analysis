using Microsoft.AspNetCore.Mvc;
using Sotd.Backend.Services;

namespace Sotd.Backend.Api;

/// <summary>
/// The endpoints every non-join table shares. The controller is only a route table: all of the behaviour
/// lives in <see cref="ICrudService{TEntity,TRead,TWrite}"/>, which knows nothing about HTTP.
/// </summary>
[ApiController]
public abstract class CrudController<TEntity, TRead, TWrite> : ControllerBase
    where TEntity : class
{
    readonly ICrudService<TEntity, TRead, TWrite> _service;

    protected CrudController(ICrudService<TEntity, TRead, TWrite> service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<PagedResult<TRead>>> List([FromQuery] ListQuery query) =>
        (await _service.ListAsync(query)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TRead>> Get(Guid id) => (await _service.GetAsync(id)).ToActionResult();

    [HttpPost]
    public async Task<ActionResult<TRead>> Create([FromBody] TWrite write) =>
        (await _service.CreateAsync(write)).ToActionResult();

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TRead>> Update(Guid id, [FromBody] TWrite write) =>
        (await _service.UpdateAsync(id, write)).ToActionResult();

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<TRead>> Delete(Guid id) =>
        (await _service.DeleteAsync(new[] { id })).ToActionResult();

    [HttpPost("delete")]
    public async Task<ActionResult<TRead>> DeleteMany([FromBody] DeleteRequest request) =>
        (await _service.DeleteAsync(request.Ids)).ToActionResult();
}
