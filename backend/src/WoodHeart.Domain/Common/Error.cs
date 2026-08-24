namespace WoodHeart.Domain.Common;

/// <summary>
/// How a failure should be represented to the caller. Maps directly onto an
/// HTTP status code in the API layer, which is the only place that knows about HTTP.
/// </summary>
public enum ErrorType
{
    /// <summary>Generic business rule violation. → 400</summary>
    Failure = 0,

    /// <summary>Input failed validation. → 400 with an <c>errors</c> dictionary</summary>
    Validation = 1,

    /// <summary>The thing being addressed does not exist. → 404</summary>
    NotFound = 2,

    /// <summary>State conflict: already used, already cancelled, concurrent edit. → 409</summary>
    Conflict = 3,

    /// <summary>Caller is not authenticated. → 401</summary>
    Unauthorized = 4,

    /// <summary>Caller is authenticated but not permitted. → 403</summary>
    Forbidden = 5,

    /// <summary>A downstream dependency failed (payment gateway, SMS). → 502</summary>
    External = 6
}

/// <summary>
/// A business failure as a value, not an exception.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Code"/> is a stable, machine-readable slug such as
/// <c>ordering.insufficient_stock</c>. It is surfaced as the RFC 9457
/// <c>type</c> so the Angular client can branch on a code instead of
/// string-matching an English message that a translator will later change.
/// </para>
/// <para>
/// Exceptions are reserved for bugs and infrastructure faults. "Coupon expired"
/// is not exceptional — it is Tuesday.
/// </para>
/// </remarks>
public sealed record Error(string Code, string Description, ErrorType Type = ErrorType.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>Field-level validation messages, keyed by property name.</summary>
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; init; }

    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);

    public static Error Validation(string code, string description,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(code, description, ErrorType.Validation) { ValidationErrors = errors };

    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    public static Error External(string code, string description) =>
        new(code, description, ErrorType.External);

    public override string ToString() => $"{Code}: {Description}";
}
