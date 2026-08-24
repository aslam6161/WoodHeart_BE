using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using WoodHeart.Application.Common.Abstractions;

namespace WoodHeart.Infrastructure.Services;

/// <summary>Reads the caller's identity from the current HTTP request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary>Custom claim carrying granular permissions such as <c>orders.refund</c>.</summary>
    public const string PermissionClaimType = "permission";

    /// <summary>Header carrying the guest cart token for an unauthenticated visitor.</summary>
    public const string AnonymousIdHeader = "X-Anonymous-Id";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name);

    public string? PhoneNumber => Principal?.FindFirstValue(ClaimTypes.MobilePhone);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public IReadOnlyCollection<string> Permissions =>
        Principal?.FindAll(PermissionClaimType).Select(c => c.Value).ToArray() ?? [];

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(PermissionClaimType, permission) ?? false;

    /// <summary>
    /// Identity for a visitor who has not logged in.
    /// </summary>
    /// <remarks>
    /// Guest checkout is a first-class path here, not a degraded one — a large
    /// share of Bangladeshi customers will never create an account. The token
    /// arrives in a header (set by the Angular client from localStorage) or a
    /// cookie, and it is what a guest cart and a guest order hang off.
    /// </remarks>
    public string? AnonymousId
    {
        get
        {
            var context = accessor.HttpContext;

            if (context is null)
            {
                return null;
            }

            if (context.Request.Headers.TryGetValue(AnonymousIdHeader, out var header)
                && !string.IsNullOrWhiteSpace(header))
            {
                return header.ToString();
            }

            return context.Request.Cookies.TryGetValue("wh_anon", out var cookie) ? cookie : null;
        }
    }
}

/// <summary>
/// Stand-in used by background jobs and the design-time EF factory, where there
/// is no HTTP request at all.
/// </summary>
public sealed class SystemUser : ICurrentUser
{
    public Guid? UserId => null;

    public string? UserName => "system";

    public string? PhoneNumber => null;

    public bool IsAuthenticated => false;

    public IReadOnlyCollection<string> Roles => [];

    public IReadOnlyCollection<string> Permissions => [];

    public bool IsInRole(string role) => false;

    public bool HasPermission(string permission) => false;

    public string? AnonymousId => null;
}
