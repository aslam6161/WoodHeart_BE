using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Settings;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Identity;
using WoodHeart.Service.DTOs.Identity;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Identity;
using WoodHeart.Service.Interfaces.Ordering;

namespace WoodHeart.Service.Services.Identity;

/// <summary>
/// Registration, sign-in, and the refresh-token rotation that carries a session.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sign-in is written out by hand rather than through
/// <c>SignInManager</c>.</b> Startup registers <c>AddIdentityCore</c>, which
/// deliberately does not bring a <c>SignInManager</c> — that class exists to
/// drive cookie authentication and two-factor UI flows this API does not have.
/// What it also owns is lockout, so lockout is handled explicitly below:
/// <see cref="UserManager{TUser}.AccessFailedAsync"/> on a wrong password,
/// <see cref="UserManager{TUser}.ResetAccessFailedCountAsync"/> on a right one.
/// Forgetting that pair is how an API ends up with a configured lockout policy
/// that never triggers.
/// </para>
/// <para>
/// <b>The two-tier token model.</b> The access token is a short-lived JWT the
/// client keeps in memory. The session lives in a refresh token that is opaque,
/// stored hashed, rotated on every use, and revocable. That combination is what
/// makes "sign this person out now" a thing the shop can actually do.
/// </para>
/// </remarks>
public class AccountService(
    UserManager<AppUser> users,
    ITokenService tokens,
    ITokenHasher hasher,
    IUserRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ICurrentUserService currentUser,
    ICartService carts,
    IOptions<JwtSettings> jwtOptions,
    ILogger<AccountService> logger) : IAccountService
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public async Task<GeneralResponse<AuthenticatedUserDto>> LoginAsync(
        LoginDto dto, CancellationToken cancellationToken = default)
    {
        if (!PhoneNumber.TryParse(dto.PhoneNumber, out var phone))
        {
            return GeneralResponse<AuthenticatedUserDto>.Fail(
                PhoneNumber.InvalidCode, PhoneNumber.InvalidMessage);
        }

        var user = await users.FindByNameAsync(phone!.Value);

        // No such account and a wrong password return the same thing. Splitting
        // them turns this endpoint into a "is this number registered?" oracle.
        if (user is null)
        {
            IdentityLog.LoginFailed(logger, phone.Masked, "no such account");

            return InvalidCredentials();
        }

        if (await users.IsLockedOutAsync(user))
        {
            IdentityLog.LoginFailed(logger, phone.Masked, "locked out");

            return GeneralResponse<AuthenticatedUserDto>.Fail(
                IdentityErrors.AccountLocked,
                "Too many failed attempts. Try again in 15 minutes.");
        }

        if (!user.IsActive)
        {
            IdentityLog.LoginFailed(logger, phone.Masked, "account disabled");

            return GeneralResponse<AuthenticatedUserDto>.Fail(
                IdentityErrors.AccountDisabled,
                "This account has been disabled. Contact the store for help.");
        }

        if (!await users.CheckPasswordAsync(user, dto.Password))
        {
            // The counter that makes the lockout policy real. Without this call
            // MaxFailedAccessAttempts is a setting that never fires.
            await users.AccessFailedAsync(user);

            IdentityLog.LoginFailed(logger, phone.Masked, "wrong password");

            return InvalidCredentials();
        }

        await users.ResetAccessFailedCountAsync(user);

        user.LastLoginAt = clock.UtcNow;
        await users.UpdateAsync(user);

        IdentityLog.LoginSucceeded(logger, phone.Masked);

        await AdoptGuestBasketAsync(user.Id, cancellationToken);

        return await IssueSessionAsync(user, dto.DeviceLabel, cancellationToken);
    }

    public async Task<GeneralResponse<AuthenticatedUserDto>> RegisterAsync(
        RegisterDto dto, CancellationToken cancellationToken = default)
    {
        if (!PhoneNumber.TryParse(dto.PhoneNumber, out var phone))
        {
            return GeneralResponse<AuthenticatedUserDto>.Invalid(
                PhoneNumber.InvalidCode,
                PhoneNumber.InvalidMessage,
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["phoneNumber"] = [PhoneNumber.InvalidMessage]
                });
        }

        string? email = null;

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            if (!EmailAddress.TryParse(dto.Email, out var parsed))
            {
                return GeneralResponse<AuthenticatedUserDto>.Invalid(
                    EmailAddress.InvalidCode,
                    EmailAddress.InvalidMessage,
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["email"] = [EmailAddress.InvalidMessage]
                    });
            }

            email = parsed!.Value;
        }

        // Checked before creating rather than relying on the unique index, so
        // the customer gets a field-level message instead of a 500.
        if (await users.FindByNameAsync(phone!.Value) is not null)
        {
            return GeneralResponse<AuthenticatedUserDto>.Invalid(
                IdentityErrors.PhoneTaken,
                "That mobile number is already registered. Sign in instead.",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["phoneNumber"] = ["That mobile number is already registered."]
                });
        }

        var user = new AppUser
        {
            UserName = phone.Value,
            PhoneNumber = phone.Value,
            Email = email,
            FullName = dto.FullName.Trim(),
            CreatedAt = clock.UtcNow,
            IsActive = true,
            PreferredLanguage = NormaliseLanguage(dto.PreferredLanguage)
        };

        var created = await users.CreateAsync(user, dto.Password);

        if (!created.Succeeded)
        {
            return GeneralResponse<AuthenticatedUserDto>.Invalid(
                IdentityErrors.RegistrationFailed,
                "We could not create the account.",
                ToFieldErrors(created));
        }

        // Everyone who registers through the storefront is a Customer. Staff
        // roles are granted from the admin panel, never claimed at sign-up —
        // a public endpoint that can hand out an Admin role is the whole game.
        await users.AddToRoleAsync(user, Roles.Customer);

        IdentityLog.Registered(logger, phone.Masked);

        await AdoptGuestBasketAsync(user.Id, cancellationToken);

        return await IssueSessionAsync(user, dto.DeviceLabel, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Rotation, and what a token seen twice means.</b> Every refresh
    /// revokes the token presented and issues a new one. So a token that
    /// arrives already revoked cannot be the legitimate holder using it a
    /// second time — either it leaked, or someone replayed a captured request.
    /// </para>
    /// <para>
    /// There is no way to tell the thief from the victim at that moment, so
    /// this revokes the whole chain and forces both to sign in again. Annoying
    /// exactly once for the real customer; a dead end for the attacker.
    /// </para>
    /// </remarks>
    public async Task<GeneralResponse<AuthenticatedUserDto>> RefreshAsync(
        string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return InvalidRefresh();
        }

        var stored = await refreshTokens.GetByHashAsync(hasher.Hash(refreshToken), cancellationToken);

        if (stored is null)
        {
            return InvalidRefresh();
        }

        var now = clock.UtcNow;

        if (stored.RevokedAt is not null)
        {
            IdentityLog.RefreshTokenReused(logger, stored.UserId, stored.Id);

            await refreshTokens.RevokeChainAsync(
                stored.Id, now, currentUser.IpAddress, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return InvalidRefresh();
        }

        if (stored.ExpiresAt <= now)
        {
            return InvalidRefresh();
        }

        var user = await users.FindByIdAsync(
            stored.UserId.ToString(CultureInfo.InvariantCulture));

        // A live token whose user has since been deleted or switched off. The
        // token is checked against the database on every refresh precisely so
        // this is caught within the access token's lifetime rather than at the
        // end of the refresh token's thirty days.
        if (user is null || !user.IsActive)
        {
            return GeneralResponse<AuthenticatedUserDto>.Fail(
                IdentityErrors.AccountDisabled,
                "This account is no longer active. Sign in again.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var result = await IssueSessionAsync(user, stored.DeviceLabel, ct);

                if (!result.IsSuccess)
                {
                    return result;
                }

                // Two saves inside one transaction: the replacement needs an id
                // before the old row can point at it. Splitting them across
                // transactions would leave a window where both tokens are live.
                stored.RevokedAt = now;
                stored.RevokedByIp = currentUser.IpAddress;
                stored.ReplacedByTokenId = result.Id;

                refreshTokens.Update(stored);
                await unitOfWork.SaveChangesAsync(ct);

                return result;
            },
            cancellationToken);
    }

    public async Task<GeneralResponse> LogoutAsync(
        string? refreshToken, CancellationToken cancellationToken = default)
    {
        // Signing out is always a success from the caller's point of view. The
        // client is clearing its cookie either way, and reporting "that token
        // was already invalid" would only ever produce a confusing error on the
        // one action that is supposed to be reassuring.
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return GeneralResponse.Success("Signed out.");
        }

        var stored = await refreshTokens.GetByHashAsync(hasher.Hash(refreshToken), cancellationToken);

        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = clock.UtcNow;
            stored.RevokedByIp = currentUser.IpAddress;

            refreshTokens.Update(stored);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return GeneralResponse.Success("Signed out.");
    }

    public async Task<GeneralResponse> LogoutEverywhereAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return GeneralResponse.Fail(
                IdentityErrors.NotAuthenticated, "You are not signed in.");
        }

        var now = clock.UtcNow;
        var active = await refreshTokens.GetActiveForUserAsync(userId, now, cancellationToken);

        foreach (var token in active)
        {
            token.RevokedAt = now;
            token.RevokedByIp = currentUser.IpAddress;
        }

        refreshTokens.UpdateRange(active);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse.Success($"Signed out of {active.Count} session(s).");
    }

    public async Task<GeneralResponse<AuthenticatedUserDto>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return GeneralResponse<AuthenticatedUserDto>.Fail(
                IdentityErrors.NotAuthenticated, "You are not signed in.");
        }

        var user = await users.FindByIdAsync(
            userId.ToString(CultureInfo.InvariantCulture));

        if (user is null || !user.IsActive)
        {
            return GeneralResponse<AuthenticatedUserDto>.Fail(
                IdentityErrors.UserNotFound, "That account no longer exists.");
        }

        var roles = await users.GetRolesAsync(user);

        // No token is minted here. This answers "who am I", and the caller
        // already holds a valid access token or it would not have reached this
        // method — issuing a fresh one would quietly extend a session that the
        // refresh endpoint is supposed to be the only way to extend.
        return GeneralResponse<AuthenticatedUserDto>.Success(
            ToDto(user, roles, accessToken: null), id: user.Id);
    }

    public async Task<GeneralResponse> ChangePasswordAsync(
        ChangePasswordDto dto, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return GeneralResponse.Fail(
                IdentityErrors.NotAuthenticated, "You are not signed in.");
        }

        var user = await users.FindByIdAsync(
            userId.ToString(CultureInfo.InvariantCulture));

        if (user is null)
        {
            return GeneralResponse.Fail(
                IdentityErrors.UserNotFound, "That account no longer exists.");
        }

        var changed = await users.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

        if (!changed.Succeeded)
        {
            return GeneralResponse.Invalid(
                IdentityErrors.PasswordChangeFailed,
                "We could not change your password.",
                ToFieldErrors(changed));
        }

        // Every other session dies with the old password. That is the entire
        // reason someone changes a password they think has leaked, and leaving
        // the attacker's session alive would make the action theatre.
        var now = clock.UtcNow;
        var active = await refreshTokens.GetActiveForUserAsync(userId, now, cancellationToken);

        foreach (var token in active)
        {
            token.RevokedAt = now;
            token.RevokedByIp = currentUser.IpAddress;
        }

        refreshTokens.UpdateRange(active);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        IdentityLog.PasswordChanged(logger, userId, active.Count);

        return GeneralResponse.Success("Password changed. Sign in again on your other devices.");
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Mints an access token and persists a fresh refresh token.
    /// </summary>
    /// <remarks>
    /// Returns the new refresh token row's id in <c>GeneralResponse.Id</c>,
    /// which <see cref="RefreshAsync"/> needs in order to point the token it is
    /// replacing at its successor.
    /// </remarks>
    /// <summary>
    /// Folds whatever the visitor had in a guest basket into their account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called on sign-in and on registration, not on refresh: a refresh is the
    /// same session continuing, and there is no guest basket left to adopt by
    /// then.
    /// </para>
    /// <para>
    /// <b>A failure here never fails the sign-in.</b> Someone who cannot get
    /// into their account is a much worse outcome than someone who has to add a
    /// lamp to their basket again, and the merge is a convenience hanging off
    /// the login rather than part of it. The exception is logged rather than
    /// swallowed silently, because a merge that stops working would otherwise
    /// look exactly like customers changing their minds.
    /// </para>
    /// </remarks>
    private async Task AdoptGuestBasketAsync(long userId, CancellationToken cancellationToken)
    {
        var anonymousId = currentUser.AnonymousId;

        if (string.IsNullOrWhiteSpace(anonymousId))
        {
            return;
        }

        try
        {
            await carts.MergeGuestCartAsync(anonymousId, userId, cancellationToken);
        }
        catch (Exception ex)
        {
            IdentityLog.GuestCartMergeFailed(logger, userId, ex);
        }
    }

    private async Task<GeneralResponse<AuthenticatedUserDto>> IssueSessionAsync(
        AppUser user, string? deviceLabel, CancellationToken cancellationToken)
    {
        var roles = await users.GetRolesAsync(user);
        var access = tokens.CreateAccessToken(user, roles);

        var refresh = tokens.CreateRefreshToken();
        var now = clock.UtcNow;
        var expiresAt = now.AddDays(_jwt.RefreshTokenDays);

        var row = new UserRefreshToken
        {
            UserId = user.Id,

            // Only the hash is stored. A leaked database backup is then a list
            // of useless digests rather than a set of live sessions.
            TokenHash = hasher.Hash(refresh),
            ExpiresAt = expiresAt,
            CreatedAt = now,
            CreatedByIp = currentUser.IpAddress,
            DeviceLabel = Truncate(deviceLabel, 128)
        };

        await refreshTokens.InsertAsync(row, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = ToDto(user, roles, access);
        dto.RefreshToken = refresh;
        dto.RefreshTokenExpiresAt = expiresAt;

        return GeneralResponse<AuthenticatedUserDto>.Success(dto, id: row.Id);
    }

    private static AuthenticatedUserDto ToDto(
        AppUser user, IEnumerable<string> roles, AccessToken? accessToken) =>
        new()
        {
            Id = user.Id,
            FullName = user.FullName,
            PhoneNumber = user.UserName ?? string.Empty,
            Email = user.Email,
            Roles = roles.ToArray(),
            AccessToken = accessToken?.Value ?? string.Empty,
            AccessTokenExpiresAt = accessToken?.ExpiresAt ?? default,
            PreferredLanguage = user.PreferredLanguage
        };

    private static string NormaliseLanguage(string? requested) =>
        GlobalConstants.SupportedLanguages.Contains(
            requested ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? requested!.ToLowerInvariant()
            : GlobalConstants.DefaultLanguage;

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= max ? value
        : value[..max];

    /// <summary>
    /// Turns Identity's errors into the per-field shape the Angular forms read.
    /// </summary>
    /// <remarks>
    /// Identity codes are things like <c>PasswordTooShort</c> and
    /// <c>DuplicateUserName</c>. Mapping them onto form control names is what
    /// puts the message under the right input instead of in a banner at the top
    /// of a form the customer has already scrolled past.
    /// </remarks>
    private static Dictionary<string, string[]> ToFieldErrors(IdentityResult result)
    {
        var byField = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var error in result.Errors)
        {
            var field = error.Code switch
            {
                var code when code.Contains("Password", StringComparison.Ordinal) => "password",
                var code when code.Contains("UserName", StringComparison.Ordinal) => "phoneNumber",
                var code when code.Contains("Email", StringComparison.Ordinal) => "email",
                _ => "general"
            };

            if (!byField.TryGetValue(field, out var messages))
            {
                messages = [];
                byField[field] = messages;
            }

            messages.Add(error.Description);
        }

        return byField.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static GeneralResponse<AuthenticatedUserDto> InvalidCredentials() =>
        GeneralResponse<AuthenticatedUserDto>.Fail(
            IdentityErrors.InvalidCredentials, "That mobile number or password is not right.");

    private static GeneralResponse<AuthenticatedUserDto> InvalidRefresh() =>
        GeneralResponse<AuthenticatedUserDto>.Fail(
            IdentityErrors.InvalidRefreshToken, "Your session has ended. Please sign in again.");
}
