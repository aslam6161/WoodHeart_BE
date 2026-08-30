using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Service.Infrastructure.Security;

/// <summary>
/// Hashes opaque tokens before storage.
/// </summary>
/// <remarks>
/// <para>
/// SHA-256, deliberately, not BCrypt. These are high-entropy random values we
/// generated ourselves, not user-chosen passwords — there is nothing to
/// brute-force, and no reason to pay a slow KDF on every token refresh.
/// Passwords are a different problem and go through ASP.NET Identity's hasher.
/// </para>
/// <para>
/// Comparison is fixed-time, because a check that returns faster for a
/// near-miss leaks the token one byte at a time.
/// </para>
/// </remarks>
public class TokenHasher : ITokenHasher
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
/// The admin pastes bKash merchant credentials into a form and they are stored
/// encrypted. They are never returned by any API — the admin UI treats those
/// fields as write-only.
/// </para>
/// <para>
/// In production the Data Protection key ring must be persisted outside the
/// container, on a mounted volume or in the database. Otherwise every
/// deployment invalidates every stored credential. That is a deployment
/// requirement documented in <c>docs/runbooks</c>, not something this code can
/// enforce.
/// </para>
/// </remarks>
public class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector =
        provider.CreateProtector("WoodHeart.PaymentCredentials.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public bool TryUnprotect(string ciphertext, out string? plaintext)
    {
        try
        {
            plaintext = _protector.Unprotect(ciphertext);
            return true;
        }
        catch (CryptographicException)
        {
            // Almost always a rotated or lost key ring rather than tampering.
            plaintext = null;
            return false;
        }
    }
}
