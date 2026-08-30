namespace WoodHeart.Domain.Constants;

/// <summary>Role names, so a typo is a compile error rather than a silent lockout.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Staff = "Staff";
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, Staff, Customer];
}

/// <summary>
/// Authorization policy names, referenced from <c>[Authorize(Policy = ...)]</c>.
/// </summary>
public static class Policies
{
    public const string RequireAdmin = "RequireAdminRole";
    public const string RequireAdminOrManager = "RequireAdminOrManagerRole";
    public const string RequireStaff = "RequireStaffRole";
    public const string RequireCustomer = "RequireCustomerRole";
}

/// <summary>Rate-limiting policy names, referenced from <c>[EnableRateLimiting(...)]</c>.</summary>
public static class RateLimitPolicies
{
    /// <summary>Browsing the storefront. Generous.</summary>
    public const string Public = "PublicPolicy";

    /// <summary>Login, OTP, password reset. Tight — these are the brute-force targets.</summary>
    public const string Sensitive = "SensitivePolicy";

    /// <summary>Checkout and payment callbacks. Queued rather than rejected.</summary>
    public const string Checkout = "CheckoutPolicy";
}
