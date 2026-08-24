using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Common.Abstractions;

/// <summary>
/// The clock, as a dependency.
/// </summary>
/// <remarks>
/// Never call <c>DateTime.UtcNow</c> in a handler or an aggregate. Consultation
/// slot generation, discount date windows, and stock-reservation expiry are all
/// time-dependent, and none of them can be tested honestly against a clock that
/// cannot be moved. <see cref="DhakaNow"/> exists because business rules such as
/// "closed on Friday" are expressed in local time, not UTC.
/// </remarks>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Current time in Asia/Dhaka (UTC+6, no daylight saving).</summary>
    DateTimeOffset DhakaNow { get; }

    DateOnly DhakaToday { get; }

    /// <summary>Converts a Dhaka-local wall-clock time to the UTC instant we store.</summary>
    DateTimeOffset DhakaToUtc(DateOnly date, TimeOnly time);

    /// <summary>Converts a stored UTC instant back to Dhaka local time for display.</summary>
    DateTimeOffset ToDhaka(DateTimeOffset utc);
}

/// <summary>Who is making the current request.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? UserName { get; }

    string? PhoneNumber { get; }

    bool IsAuthenticated { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool IsInRole(string role);

    bool HasPermission(string permission);

    /// <summary>
    /// Cart token for an unauthenticated visitor. Guest checkout is a first-class
    /// path here, not a degraded one, so anonymous callers still have an identity.
    /// </summary>
    string? AnonymousId { get; }
}

/// <summary>Correlates one logical operation across API logs, jobs and gateway calls.</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}

/// <summary>Enqueues background work. Wraps Hangfire so handlers never reference it directly.</summary>
public interface IBackgroundJobScheduler
{
    string Enqueue<TJob>(Func<TJob, Task> job) where TJob : class;

    string Schedule<TJob>(Func<TJob, Task> job, TimeSpan delay) where TJob : class;

    bool Delete(string jobId);
}

/// <summary>Reads runtime feature flags such as <c>bkash.enabled</c>.</summary>
public interface IFeatureFlagService
{
    Task<bool> IsEnabledAsync(string feature, CancellationToken cancellationToken = default);
}

/// <summary>
/// Generates the human-facing sequential identifiers customers quote on the
/// phone: <c>WH-2608-00042</c>. Backed by a database sequence, so it is
/// gap-tolerant but never duplicated.
/// </summary>
public interface INumberSequenceGenerator
{
    Task<string> NextOrderNumberAsync(CancellationToken cancellationToken = default);

    Task<string> NextBookingNumberAsync(CancellationToken cancellationToken = default);

    Task<string> NextInvoiceNumberAsync(CancellationToken cancellationToken = default);
}

/// <summary>Hashes and verifies opaque tokens (refresh tokens, guest cart tokens).</summary>
public interface ITokenHasher
{
    string Hash(string token);

    bool Verify(string token, string hash);
}

/// <summary>
/// Encrypts payment-gateway credentials at rest. Implemented with ASP.NET Core
/// Data Protection; the plaintext never leaves the server and is never returned
/// by any API.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    Result<string> Unprotect(string ciphertext);
}
