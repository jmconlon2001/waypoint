namespace Waypoint.Api.Services;

/// <summary>Thrown by services; turned into an RFC 7807 problem response by the exception handler.</summary>
public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static ApiException NotFound(string what) => new(StatusCodes.Status404NotFound, $"{what} was not found.");
    public static ApiException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
    /// <summary>An upstream service (Google Calendar) failed.</summary>
    public static ApiException BadGateway(string message) => new(StatusCodes.Status502BadGateway, message);
}
