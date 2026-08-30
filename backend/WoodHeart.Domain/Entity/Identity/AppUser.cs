using Microsoft.AspNetCore.Identity;

namespace WoodHeart.Domain.Entity.Identity;

/// <summary>
/// The authentication record. Deliberately thin.
/// </summary>
/// <remarks>
/// <para>
/// This is the sign-in credential, not the shopper. The shopper is
/// <c>Entity/Ordering.Customer</c>, which owns addresses, order history and
/// consultation bookings — and which exists perfectly well for someone who has
/// never set a password.
/// </para>
/// <para>
/// Keeping the two apart is what makes guest checkout a normal path rather than
/// a special case: a guest order has a Customer and no AppUser, and claiming it
/// later just links the two by phone number.
/// </para>
/// <para>
/// <see cref="IdentityUser{TKey}.UserName"/> holds the E.164 phone number,
/// because phone is the login handle in this market. Email is optional and many
/// customers will never supply one.
/// </para>
/// </remarks>
public class AppUser : IdentityUser<long>
{
    /// <summary>Links to the <c>Customer</c> record. Null for staff accounts.</summary>
    public long? CustomerId { get; set; }

    public string? FullName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Set false to block sign-in without deleting history.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary><c>en</c> or <c>bn</c> — drives SMS and email language.</summary>
    public string PreferredLanguage { get; set; } = "en";

    public ICollection<AppUserRole> UserRoles { get; set; } = [];

    public ICollection<UserRefreshToken> RefreshTokens { get; set; } = [];
}

/// <summary>A role, extended with a description so the admin UI can explain itself.</summary>
public class AppRole : IdentityRole<long>
{
    public string? Description { get; set; }

    /// <summary>Built-in roles cannot be renamed or deleted from the admin UI.</summary>
    public bool IsSystemRole { get; set; }

    public ICollection<AppUserRole> UserRoles { get; set; } = [];
}

/// <summary>
/// The user-role join, made explicit so both navigation properties exist.
/// </summary>
/// <remarks>
/// Identity's default join entity has no navigations, which forces every "list
/// the staff and their roles" screen into either a second round trip or a
/// UserManager call per row.
/// </remarks>
public class AppUserRole : IdentityUserRole<long>
{
    public AppUser User { get; set; } = null!;

    public AppRole Role { get; set; } = null!;
}
