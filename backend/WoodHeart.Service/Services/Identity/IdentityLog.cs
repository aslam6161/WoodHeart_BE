using Microsoft.Extensions.Logging;

namespace WoodHeart.Service.Services.Identity;

/// <summary>
/// Source-generated logging for authentication, matching <c>MediaLog</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phone numbers are logged masked, never in full.</b> Every parameter named
/// <c>phone</c> below is fed <c>PhoneNumber.Masked</c> — <c>017****5678</c>.
/// The customer list of a furniture shop is worth something to a competitor,
/// and log files travel: they get shipped to aggregators, attached to support
/// tickets, and read by whoever is on call. Enough of the number survives to
/// match against a customer who is on the phone; not enough to dial.
/// </para>
/// <para>
/// <see cref="RefreshTokenReused"/> is the one worth alerting on. A rotated
/// refresh token arriving a second time is not something a working client ever
/// does — it means the token leaked, and this is the only place that says so.
/// </para>
/// </remarks>
internal static partial class IdentityLog
{
    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Information,
        Message = "Sign-in succeeded for {Phone}.")]
    public static partial void LoginSucceeded(ILogger logger, string phone);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Warning,
        Message = "Sign-in failed for {Phone}: {Reason}")]
    public static partial void LoginFailed(ILogger logger, string phone, string reason);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Information,
        Message = "Registered a new account for {Phone}.")]
    public static partial void Registered(ILogger logger, string phone);

    [LoggerMessage(
        EventId = 1303,
        Level = LogLevel.Error,
        Message = "A revoked refresh token was presented for user {UserId} (token {TokenId}). "
                  + "The token chain has been revoked — treat this as a possible theft.")]
    public static partial void RefreshTokenReused(ILogger logger, long userId, long tokenId);

    [LoggerMessage(
        EventId = 1304,
        Level = LogLevel.Information,
        Message = "User {UserId} changed their password; {SessionCount} other session(s) revoked.")]
    public static partial void PasswordChanged(ILogger logger, long userId, int sessionCount);
}
