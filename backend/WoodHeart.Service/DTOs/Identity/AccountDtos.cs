using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace WoodHeart.Service.DTOs.Identity;

/// <summary>
/// Sign-in credentials.
/// </summary>
/// <remarks>
/// The handle is a phone number, not an email. That is not a preference: a
/// large share of this market shops without an email address they check, and
/// the number is already how the delivery rider reaches them.
/// </remarks>
public class LoginDto
{
    /// <summary>
    /// Any format the customer types. The service normalises it.
    /// </summary>
    /// <remarks>
    /// <c>01712345678</c>, <c>+8801712345678</c> and <c>017-1234-5678</c> are
    /// the same account, and a login form that disagrees is a support call.
    /// </remarks>
    [Required(ErrorMessage = "Enter your mobile number.")]
    [StringLength(24)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [StringLength(128, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional, shown in a future "your devices" screen. Never trusted.</summary>
    [StringLength(128)]
    public string? DeviceLabel { get; set; }
}

public class RegisterDto
{
    [Required(ErrorMessage = "Enter your name.")]
    [StringLength(200, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your mobile number.")]
    [StringLength(24)]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Optional throughout WoodHeart. The phone number is the required channel.</summary>
    [StringLength(254)]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    public string Password { get; set; } = string.Empty;

    /// <summary><c>en</c> or <c>bn</c>. Drives the language of every SMS they receive.</summary>
    [StringLength(2)]
    public string? PreferredLanguage { get; set; }

    [StringLength(128)]
    public string? DeviceLabel { get; set; }
}

public class ChangePasswordDto
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// Who signed in, and the token that proves it.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what the browser is told about a session. There is no
/// refresh token in the serialised body — see <see cref="RefreshToken"/>.
/// </para>
/// <para>
/// <see cref="Roles"/> travels so the client can hide an admin link it would
/// not be allowed to follow. It is a rendering hint and nothing more: every
/// admin endpoint checks the token's own role claims server-side, so a user who
/// edits this array in memory gains a menu item and a 403.
/// </para>
/// </remarks>
public class AuthenticatedUserDto
{
    public long Id { get; set; }

    public string? FullName { get; set; }

    /// <summary>E.164 — <c>+8801712345678</c>. The login handle.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public string? Email { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = [];

    /// <summary>The JWT. Short-lived; the refresh cookie carries the session.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// When the access token stops working.
    /// </summary>
    /// <remarks>
    /// Sent so the client can refresh a minute early rather than discovering
    /// expiry as a failed request in the middle of saving a product.
    /// </remarks>
    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    public string PreferredLanguage { get; set; } = "en";

    /// <summary>
    /// The rotated refresh token, for the controller to put in a cookie.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="JsonIgnoreAttribute"/> is the point of this property.</b>
    /// The service produces the token and the controller writes it into an
    /// <c>HttpOnly</c> cookie; it must never appear in a response body, because
    /// a body is readable by any script on the page and the cookie exists
    /// precisely so a stolen script cannot read it.
    /// </para>
    /// <para>
    /// Carrying it here rather than in a tuple means that guarantee is a
    /// property of the type rather than of every controller remembering. A
    /// future endpoint that returns this DTO cannot leak the token by
    /// forgetting to unwrap something.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public string? RefreshToken { get; set; }

    /// <summary>How long the refresh cookie should live. Not serialised.</summary>
    [JsonIgnore]
    public DateTimeOffset RefreshTokenExpiresAt { get; set; }
}
