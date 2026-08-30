namespace WoodHeart.Domain.Entity.Identity;

/// <summary>
/// A rotating refresh token.
/// </summary>
/// <remarks>
/// <para>
/// Stored hashed, never in plaintext: a leaked database must not hand an
/// attacker a drawer full of working sessions.
/// </para>
/// <para>
/// Rotation on every use means a stolen token is good for one request at most,
/// and presenting an already-rotated token is a theft signal — the correct
/// response is to revoke the entire chain, which is what
/// <see cref="ReplacedByTokenId"/> makes traceable.
/// </para>
/// </remarks>
public class UserRefreshToken
{
    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary>SHA-256 of the token. The plaintext exists only in the response body.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedByIp { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedByIp { get; set; }

    /// <summary>The token that superseded this one — lets us walk a reuse attack back.</summary>
    public long? ReplacedByTokenId { get; set; }

    /// <summary>Which device this session belongs to, so "sign out everywhere else" is possible.</summary>
    public string? DeviceLabel { get; set; }

    public AppUser? User { get; set; }
}
