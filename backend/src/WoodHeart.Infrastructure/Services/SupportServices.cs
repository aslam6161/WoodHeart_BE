using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using WoodHeart.Application.Common.Abstractions;
using WoodHeart.Domain.Common;

namespace WoodHeart.Infrastructure.Services;

/// <summary>
/// Supplies the id that ties an Angular request, the API logs, a Hangfire job
/// and an outbound bKash call into one traceable story.
/// </summary>
/// <remarks>
/// Falls back to the current <see cref="Activity"/> id so background work
/// started by OpenTelemetry still correlates, and finally to a fresh id so this
/// property is never null and no log line is ever orphaned.
/// </remarks>
public sealed class CorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";

    public string CorrelationId =>
        accessor.HttpContext?.Items[HeaderName] as string
        ?? accessor.HttpContext?.TraceIdentifier
        ?? Activity.Current?.Id
        ?? Guid.CreateVersion7().ToString("N");
}

/// <summary>
/// Hashes opaque tokens (refresh tokens, guest cart tokens) before storage.
/// </summary>
/// <remarks>
/// <para>
/// SHA-256, deliberately, not BCrypt. These are high-entropy random values we
/// generated ourselves, not user-chosen passwords, so there is nothing to
/// brute-force and no reason to pay a slow KDF on every token refresh.
/// Passwords are a different problem and go through ASP.NET Identity's hasher.
/// </para>
/// <para>
/// Comparison is fixed-time, because a token check that returns faster for a
/// near-miss leaks the token one byte at a time.
/// </para>
/// </remarks>
public sealed class TokenHasher : ITokenHasher
{
    public string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public bool Verify(string token, string hash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(token)),
            Encoding.UTF8.GetBytes(hash));

    /// <summary>Generates a new 256-bit URL-safe token.</summary>
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}

/// <summary>
/// Encrypts payment-gateway credentials at rest using ASP.NET Data Protection.
/// </summary>
/// <remarks>
/// <para>
/// The admin can paste bKash merchant credentials into a form, and they are
/// stored encrypted rather than in plaintext. They are never returned by any
/// API — the admin UI treats those fields as write-only.
/// </para>
/// <para>
/// In production the Data Protection key ring must be persisted outside the
/// container (a mounted volume or the database) or every deployment invalidates
/// every stored credential. That is a deployment requirement, documented in
/// <c>docs/runbooks</c>, not something the code can enforce.
/// </para>
/// </remarks>
public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("WoodHeart.PaymentCredentials.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public Result<string> Unprotect(string ciphertext)
    {
        try
        {
            return Result.Success(_protector.Unprotect(ciphertext));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Almost always a rotated or lost key ring rather than tampering.
            // Returning a Result lets the admin UI say "re-enter your
            // credentials" instead of throwing a 500 at a shop manager.
            return Result.Failure<string>(Error.Failure(
                "security.secret_unreadable",
                "This stored credential could not be decrypted. Please re-enter it."));
        }
    }
}
