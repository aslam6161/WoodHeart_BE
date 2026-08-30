namespace WoodHeart.Service.Interfaces.Common;

/// <summary>
/// Who is making the current request.
/// </summary>
/// <remarks>
/// <see cref="AnonymousId"/> is what makes a guest basket work. A shopper who
/// has not signed in still needs a stable identity for the length of their
/// visit, and a large share of orders here will be placed by exactly that
/// person — guest checkout is the main path, not an edge case.
/// </remarks>
public interface ICurrentUserService
{
    long? UserId { get; }

    /// <summary>The E.164 phone number, which is what <c>UserName</c> holds.</summary>
    string? PhoneNumber { get; }

    bool IsAuthenticated { get; }

    IReadOnlyList<string> Roles { get; }

    bool IsInRole(string role);

    /// <summary>Stable id for a signed-out visitor, from a header or cookie.</summary>
    string? AnonymousId { get; }

    string? IpAddress { get; }

    /// <summary><c>en</c> or <c>bn</c>, from the Accept-Language header or the user's profile.</summary>
    string Language { get; }
}

/// <summary>
/// The id that ties an Angular request, the API logs, a Hangfire job and an
/// outbound bKash call into one traceable story.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}

/// <summary>Hashes opaque tokens — refresh tokens, guest cart tokens — before storage.</summary>
public interface ITokenHasher
{
    string Hash(string token);

    bool Verify(string token, string hash);
}

/// <summary>Encrypts payment-gateway credentials at rest.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts, or returns false if the key ring has rotated or been lost.
    /// </summary>
    /// <remarks>
    /// A failure here is nearly always a lost key ring rather than tampering.
    /// Returning a bool lets the admin UI say "re-enter your credentials"
    /// instead of throwing a 500 at a shop manager.
    /// </remarks>
    bool TryUnprotect(string ciphertext, out string? plaintext);
}

/// <summary>Reads runtime settings, cached. Writes go through the admin service.</summary>
public interface IStoreSettingService
{
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    Task<decimal> GetDecimalAsync(
        string key, decimal fallback = 0m, CancellationToken cancellationToken = default);

    Task<int> GetIntAsync(string key, int fallback = 0, CancellationToken cancellationToken = default);

    Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken cancellationToken = default);

    /// <summary>Call after a write so the next read does not serve a stale value.</summary>
    void Invalidate(string key);
}

/// <summary>Reads feature flags, cached aggressively — these sit on hot paths.</summary>
public interface IFeatureFlagService
{
    Task<bool> IsEnabledAsync(string name, CancellationToken cancellationToken = default);

    void Invalidate(string name);
}

/// <summary>
/// Generates the human-facing sequential numbers customers and couriers quote.
/// </summary>
/// <remarks>
/// Separate from the database id on purpose. <c>WH-2608-00042</c> can be read
/// down a phone line, sorts by month, and does not tell a competitor how many
/// orders were placed last week — all things a raw identity column fails at.
/// </remarks>
public interface INumberSequenceService
{
    Task<string> NextAsync(string sequenceName, string prefix, CancellationToken cancellationToken = default);
}
