namespace WoodHeart.Repository;

/// <summary>
/// The standard result of a service operation that can fail for business
/// reasons.
/// </summary>
/// <remarks>
/// <para>
/// Expected failures — "coupon expired", "only 2 left in stock", "that phone
/// number is already registered" — are returned as values, not thrown.
/// Exceptions are reserved for bugs and infrastructure faults, which is what
/// lets the exception middleware treat everything it catches as a 500 worth
/// paging someone about.
/// </para>
/// <para>
/// <b><see cref="ErrorCode"/> is the part that matters to the frontend.</b>
/// Angular branches on the code, never on <see cref="Message"/>: the message is
/// prose, gets reworded, and gets translated to Bangla. A code like
/// <c>ordering.insufficient_stock</c> is a contract.
/// </para>
/// </remarks>
public class GeneralResponse
{
    /// <summary>The id of the affected row, where the operation created or changed one.</summary>
    public long Id { get; set; }

    public object? Data { get; set; }

    public bool IsSuccess { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>Stable machine-readable code, e.g. <c>catalog.slug_taken</c>. Null on success.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Per-field messages for form display. Keys are camelCase to match the Angular form controls.</summary>
    public IDictionary<string, string[]>? Errors { get; set; }

    public static GeneralResponse Success(string message = "", long id = 0, object? data = null) =>
        new() { IsSuccess = true, Message = message, Id = id, Data = data };

    public static GeneralResponse Fail(string errorCode, string message) =>
        new() { IsSuccess = false, ErrorCode = errorCode, Message = message };

    public static GeneralResponse Invalid(string errorCode, string message, IDictionary<string, string[]> errors) =>
        new() { IsSuccess = false, ErrorCode = errorCode, Message = message, Errors = errors };
}

/// <summary>
/// A <see cref="GeneralResponse"/> whose payload is typed.
/// </summary>
/// <remarks>
/// Bento's <c>GeneralResponse</c> carries <c>object Data</c>, so every caller
/// casts and the controller's OpenAPI schema says nothing useful. This generic
/// version costs nothing and gives the generated Angular client a real type.
/// Prefer it; the untyped one stays for operations that return only an id.
/// </remarks>
public class GeneralResponse<T> : GeneralResponse
{
    public new T? Data
    {
        get => (T?)base.Data;
        set => base.Data = value;
    }

    public static GeneralResponse<T> Success(T data, string message = "", long id = 0) =>
        new() { IsSuccess = true, Message = message, Id = id, Data = data };

    public static new GeneralResponse<T> Fail(string errorCode, string message) =>
        new() { IsSuccess = false, ErrorCode = errorCode, Message = message };

    public static new GeneralResponse<T> Invalid(
        string errorCode, string message, IDictionary<string, string[]> errors) =>
        new() { IsSuccess = false, ErrorCode = errorCode, Message = message, Errors = errors };
}
