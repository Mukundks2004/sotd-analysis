using Microsoft.AspNetCore.Mvc;
using Sotd.Backend.Services;

namespace Sotd.Backend.Api;

/// <summary>The one place a service outcome turns into an HTTP status code.</summary>
public static class ServiceResults
{
    public static ActionResult<T> ToActionResult<T>(this ServiceResult<T> result) => Translate(result);

    static ActionResult Translate<T>(ServiceResult<T> result) => result.Status switch
    {
        ResultStatus.Ok => new OkObjectResult(result.Value),
        ResultStatus.NoContent => new NoContentResult(),
        ResultStatus.NotFound => new NotFoundObjectResult(Error(result)),
        ResultStatus.Conflict => new ConflictObjectResult(Error(result)),
        _ => new BadRequestObjectResult(Error(result)),
    };

    static ApiError Error<T>(ServiceResult<T> result) =>
        new(result.Message ?? "Something went wrong.", result.Detail);
}
