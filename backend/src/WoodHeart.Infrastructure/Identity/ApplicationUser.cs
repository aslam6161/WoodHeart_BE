using Microsoft.AspNetCore.Identity;

namespace WoodHeart.Infrastructure.Identity;

/// <summary>
/// The authentication record. Deliberately thin.
/// </summary>
/// <remarks>
/// <para>
/// This is an <em>infrastructure</em> concern, not a domain one. The domain's
/// notion of a person who buys furniture is <c>Catalog/Identity.Customer</c>,
/// which owns addresses, order history and consultation bookings, and which
/// exists perfectly well for a guest who has never created a password.
/// </para>
/// <para>
/// Keeping them separate is what makes guest checkout a first-class path rather
/// than a special case: a guest order has a Customer and no ApplicationUser,
/// and claiming it later just links the two.
/// </para>
/// <para>
/// <see cref="IdentityUser{TKey}.UserName"/> holds the E.164 phone number,
/// because phone is the login handle in this market. Email is optional.
/// </para>
/// </remarks>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Links to the domain <c>Customer</c> aggregate. Null for staff accounts.</summary>
    public Guid? CustomerId { get; set; }

    public string? FullName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    /// <summary>Set false to block sign-in without deleting history.</summary>
    public bool IsActive { get; set; } = true;

    public string? PreferredLanguage { get; set; } = "en";
}

/// <summary>A role, extended with a description so the admin UI can explain itself.</summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    public string? Description { get; set; }

    /// <summary>Built-in roles cannot be renamed or deleted from the admin UI.</summary>
    public bool IsSystemRole { get; set; }
}

/// <summary>
/// A rotating refresh token.
/// </summary>
/// <remarks>
/// Stored hashed, never in plaintext: a leaked database must not hand an
/// attacker working sessions. Rotation on every use means a stolen token is
/// good for one request at most, and reuse of an already-rotated token is a
/// detectable theft signal that revokes the whole family.
/// </remarks>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? CreatedByIp { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public string? RevokedByIp { get; set; }

    /// <summary>The token that replaced this one — lets us trace a reuse attack.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive => RevokedAtUtc is null && DateTimeOffset.UtcNow < ExpiresAtUtc;

    public ApplicationUser? User { get; set; }
}

/// <summary>Static role names, so a typo is a compile error rather than a silent lockout.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Staff = "Staff";
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, Staff, Customer];
}
