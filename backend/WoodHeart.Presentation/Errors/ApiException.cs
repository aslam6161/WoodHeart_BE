namespace WoodHeart.Presentation.Errors;

/// <summary>
/// The body returned for an unhandled exception.
/// </summary>
/// <remarks>
/// <see cref="Details"/> is populated in Development only. A stack trace in a
/// production response tells an attacker the framework versions, the file
/// layout and often the connection string.
/// </remarks>
public class ApiException(int statusCode, string message, string? correlationId = null, string? details = null)
{
    public int StatusCode { get; } = statusCode;

    public string Message { get; } = message;

    /// <summary>Give this to support — it finds the request in the logs.</summary>
    public string? CorrelationId { get; } = correlationId;

    public string? Details { get; } = details;

    /// <summary>Kept so the client's error interceptor sees one shape, always.</summary>
    public bool IsSuccess => false;
}
