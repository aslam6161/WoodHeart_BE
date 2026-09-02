namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for sign-in, registration and session management.
/// </summary>
/// <remarks>
/// <para>
/// Same contract as <see cref="CatalogErrors"/>: the code is what the Angular
/// client branches on, the message is prose that will be reworded and
/// translated. The suffix picks the HTTP status in
/// <c>BaseApiController.HandleResult</c>, so <c>.unauthorized</c> and
/// <c>.forbidden</c> here are load-bearing rather than decorative.
/// </para>
/// <para>
/// <b>401 and 403 are used for different things and the difference matters to
/// the client.</b> A 401 means "we do not know who you are" and the interceptor
/// should try a refresh; a 403 means "we know exactly who you are and the
/// answer is still no", and retrying is pointless. Returning 401 for a disabled
/// account would put the client into a refresh loop it can never win.
/// </para>
/// </remarks>
public static class IdentityErrors
{
    private const string Prefix = "identity.";

    /// <summary>
    /// Wrong password, or no such account.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately one code for both.</b> Separating them turns the login
    /// form into an oracle for "is this phone number registered here?", which
    /// for a furniture shop's customer list is a real privacy leak and a
    /// starting point for a targeted attack. The message says the same thing in
    /// both cases too — a distinct message defeats the shared code.
    /// </remarks>
    public const string InvalidCredentials = Prefix + "invalid_credentials.unauthorized";

    /// <summary>Too many failed attempts. Identity's lockout, not ours.</summary>
    public const string AccountLocked = Prefix + "account_locked.forbidden";

    /// <summary>
    /// <c>AppUser.IsActive</c> is false — an account switched off rather than
    /// deleted, so its order history survives.
    /// </summary>
    public const string AccountDisabled = Prefix + "account_disabled.forbidden";

    public const string PhoneTaken = Prefix + "phone_taken";

    /// <summary>Identity refused the password or the user shape. Carries field errors.</summary>
    public const string RegistrationFailed = Prefix + "registration_failed";

    /// <summary>
    /// No such refresh token, or one that has been revoked, expired or already
    /// rotated. All four are the same answer to the client: sign in again.
    /// </summary>
    public const string InvalidRefreshToken = Prefix + "invalid_refresh_token.unauthorized";

    /// <summary>A valid token whose user has since been deleted or disabled.</summary>
    public const string UserNotFound = Prefix + "user.not_found";

    public const string PasswordChangeFailed = Prefix + "password_change_failed";

    /// <summary>The caller has no identity at all — an endpoint reached without a token.</summary>
    public const string NotAuthenticated = Prefix + "not_authenticated.unauthorized";
}
