namespace WoodHeart.Presentation.Errors;

/// <summary>
/// The body returned for an unhandled exception.
/// </summary>
/// <remarks>
/// <see cref="Details"/> is populated in Development only. A stack trace in a
/// production response tells an attacker the framework versions, the file
/// layout and often the connection string.
/// </remarks>
public class ApiException(
    int statusCode,
    string message,
    string? correlationId = null,
    string? details = null,
    string? errorCode = null)
{
    public int StatusCode { get; } = statusCode;

    public string Message { get; } = message;

    /// <summary>
    /// The same stable code a <c>GeneralResponse</c> carries, where the fault
    /// has one.
    /// </summary>
    /// <remarks>
    /// Null for the ordinary unhandled exception, which has nothing useful to
    /// say. It is set where the database itself refused something the client
    /// could act on — two customers racing for one consultation slot — so the
    /// client branches on the same code it would have got had the service
    /// caught it first.
    /// </remarks>
    public string? ErrorCode { get; } = errorCode;

    /// <summary>Give this to support — it finds the request in the logs.</summary>
    public string? CorrelationId { get; } = correlationId;

    public string? Details { get; } = details;

    /// <summary>Kept so the client's error interceptor sees one shape, always.</summary>
    public bool IsSuccess => false;
}
