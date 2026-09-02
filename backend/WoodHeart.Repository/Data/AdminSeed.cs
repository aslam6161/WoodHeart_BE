using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Repository.Data;

/// <summary>
/// The first administrator, so a fresh deployment has someone who can sign in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bootstrapping problem this solves.</b> Staff roles are granted from
/// the admin panel and never claimed at registration — a public endpoint that
/// can hand out an Admin role is the whole game. Which leaves a new database
/// with no administrator and no way to create one, so the first account has to
/// come from outside the application.
/// </para>
/// <para>
/// <b>The credentials come from configuration and are never committed.</b>
/// <c>Seed:Admin:PhoneNumber</c> and <c>Seed:Admin:Password</c> — user-secrets
/// locally, environment variables in production. When either is missing this
/// does nothing and says so, which is the correct behaviour for a CI run or a
/// checkout that has not been configured yet. A hard-coded fallback password
/// would be in the repository, in the container image, and on every deployment
/// that forgot to override it.
/// </para>
/// <para>
/// <b>An existing account is never modified beyond its role.</b> Re-running
/// this must not reset a password an administrator has since changed, and must
/// not silently restore access to an account somebody deliberately disabled.
/// </para>
/// </remarks>
public static class AdminSeed
{
    public const string PhoneKey = "Seed:Admin:PhoneNumber";
    public const string PasswordKey = "Seed:Admin:Password";

    public static async Task RunAsync(
        UserManager<AppUser> users,
        IDateTimeProvider clock,
        string? phoneNumber,
        string? password,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(password))
        {
            SeedLog.AdminNotConfigured(logger, PhoneKey, PasswordKey);

            return;
        }

        if (!PhoneNumber.TryParse(phoneNumber, out var phone))
        {
            SeedLog.AdminPhoneInvalid(logger, PhoneKey);

            return;
        }

        var existing = await users.FindByNameAsync(phone!.Value);

        if (existing is not null)
        {
            // Only the role is topped up. The account is otherwise left exactly
            // as the administrator has it.
            if (!await users.IsInRoleAsync(existing, Roles.Admin))
            {
                await users.AddToRoleAsync(existing, Roles.Admin);

                SeedLog.AdminRoleRestored(logger, phone.Masked);
            }

            return;
        }

        var admin = new AppUser
        {
            UserName = phone.Value,
            PhoneNumber = phone.Value,
            PhoneNumberConfirmed = true,
            FullName = "WoodHeart Administrator",
            CreatedAt = clock.UtcNow,
            IsActive = true,
            PreferredLanguage = GlobalConstants.DefaultLanguage
        };

        var created = await users.CreateAsync(admin, password);

        if (!created.Succeeded)
        {
            // Nearly always a password that fails the length rule. Logged rather
            // than thrown: a bad seed value must not stop the API booting, or a
            // typo in an environment variable takes the whole store offline.
            SeedLog.AdminCreateFailed(
                logger,
                string.Join("; ", created.Errors.Select(error => error.Description)));

            return;
        }

        await users.AddToRoleAsync(admin, Roles.Admin);

        SeedLog.AdminSeeded(logger, phone.Masked);
    }
}

/// <summary>Source-generated seeding log, matching <c>StartupLog</c>.</summary>
internal static partial class SeedLog
{
    [LoggerMessage(
        EventId = 1400,
        Level = LogLevel.Warning,
        Message = "No administrator is configured, so none was seeded. Set {PhoneKey} and "
                  + "{PasswordKey} to create the first sign-in.")]
    public static partial void AdminNotConfigured(ILogger logger, string phoneKey, string passwordKey);

    [LoggerMessage(
        EventId = 1401,
        Level = LogLevel.Warning,
        Message = "{PhoneKey} is not a valid Bangladeshi mobile number. No administrator seeded.")]
    public static partial void AdminPhoneInvalid(ILogger logger, string phoneKey);

    [LoggerMessage(
        EventId = 1402,
        Level = LogLevel.Warning,
        Message = "Seeded the first administrator ({Phone}). Change this password before go-live.")]
    public static partial void AdminSeeded(ILogger logger, string phone);

    [LoggerMessage(
        EventId = 1403,
        Level = LogLevel.Warning,
        Message = "Granted the Admin role back to the configured account ({Phone}).")]
    public static partial void AdminRoleRestored(ILogger logger, string phone);

    [LoggerMessage(
        EventId = 1404,
        Level = LogLevel.Error,
        Message = "Could not create the seeded administrator: {Reason}")]
    public static partial void AdminCreateFailed(ILogger logger, string reason);
}
