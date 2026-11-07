namespace Sotd.Backend.Services;

/// <summary>How a service call ended. Deliberately free of ASP.NET types so services can be tested on their own.</summary>
public enum ResultStatus
{
    Ok,
    NoContent,
    NotFound,
    Conflict,
    BadRequest,
}

/// <summary>
/// The outcome of a service call: either a value, or a message explaining why there isn't one. The
/// controller is the only place that turns this into an HTTP status code.
/// </summary>
public record ServiceResult<T>(ResultStatus Status, T? Value, string? Message = null, string? Detail = null)
{
    public bool IsOk => Status is ResultStatus.Ok or ResultStatus.NoContent;

    public static ServiceResult<T> Ok(T value) => new(ResultStatus.Ok, value);
    public static ServiceResult<T> NoContent() => new(ResultStatus.NoContent, default);
    public static ServiceResult<T> NotFound(string message) => new(ResultStatus.NotFound, default, message);
    public static ServiceResult<T> Conflict(string message, string? detail = null) => new(ResultStatus.Conflict, default, message, detail);
    public static ServiceResult<T> BadRequest(string message) => new(ResultStatus.BadRequest, default, message);
}
