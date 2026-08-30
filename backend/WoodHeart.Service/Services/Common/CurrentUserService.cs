using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Service.Services.Common;

/// <inheritdoc />
public class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public long? UserId =>
        long.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? PhoneNumber => User?.FindFirstValue(ClaimTypes.Name);

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyList<string> Roles =>
        User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;

    /// <summary>
    /// A signed-out visitor's stable id, from the header the Angular client
    /// sends or the cookie the API set.
    /// </summary>
    /// <remarks>
    /// This is what makes a guest basket survive a page reload, and guest
    /// checkout is the main path here rather than an edge case.
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

            var fromHeader = context.Request.Headers[GlobalConstants.AnonymousIdHeader].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(fromHeader))
            {
                return fromHeader;
            }

            return context.Request.Cookies.TryGetValue(GlobalConstants.AnonymousIdCookie, out var cookie)
                ? cookie
                : null;
        }
    }

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string Language
    {
        get
        {
            var requested = accessor.HttpContext?.Request.Headers.AcceptLanguage.FirstOrDefault();

            return requested is not null
                   && requested.Contains(GlobalConstants.BanglaLanguage, StringComparison.OrdinalIgnoreCase)
                ? GlobalConstants.BanglaLanguage
                : GlobalConstants.DefaultLanguage;
        }
    }
}

/// <summary>
/// The identity background jobs run as.
/// </summary>
/// <remarks>
/// Hangfire jobs and the seeder have no HTTP context, and audit rows that say
/// "system" are more honest than rows that say nothing.
/// </remarks>
public class SystemUserService : ICurrentUserService
{
    public long? UserId => null;

    public string? PhoneNumber => null;

    public bool IsAuthenticated => false;

    public IReadOnlyList<string> Roles => [Roles_.Admin];

    public bool IsInRole(string role) => string.Equals(role, Roles_.Admin, StringComparison.Ordinal);

    public string? AnonymousId => null;

    public string? IpAddress => null;

    public string Language => GlobalConstants.DefaultLanguage;
}

// Aliased so the class above can name the role constants without colliding with
// its own Roles property.
file static class Roles_
{
    public const string Admin = WoodHeart.Domain.Constants.Roles.Admin;
}
