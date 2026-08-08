using System.Text.Json.Serialization;

// Centralized error types, mirroring errors.go in the Go version.
// The HTTP status code is set on the response but not repeated in the body.

public class ApiError
{
    [JsonIgnore]
    public int StatusCode { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    public IResult Send() =>
        Results.Json(new ErrorResponse { Error = this }, statusCode: StatusCode);

    public static ApiError NewBadRequestError(string message) => new()
    {
        StatusCode = StatusCodes.Status400BadRequest,
        Code = "BAD_REQUEST",
        Message = message,
    };

    public static ApiError NewNotFoundError(string resource) => new()
    {
        StatusCode = StatusCodes.Status404NotFound,
        Code = "NOT_FOUND",
        Message = resource + " not found",
    };

    public static ApiError NewConflictError(string message) => new()
    {
        StatusCode = StatusCodes.Status409Conflict,
        Code = "CONFLICT",
        Message = message,
    };

    public static ApiError NewInternalError() => new()
    {
        StatusCode = StatusCodes.Status500InternalServerError,
        Code = "INTERNAL_ERROR",
        Message = "An internal server error occurred",
    };
}

public class ErrorResponse
{
    [JsonPropertyName("error")]
    public ApiError Error { get; init; } = new();
}
