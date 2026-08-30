using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;

namespace WoodHeart.Presentation.Extensions;

public static class IdentityServiceExtension
{
    public static WebApplicationBuilder AddIdentityService(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration
            .GetSection(JwtSettings.SectionName)
            .Get<JwtSettings>() ?? new JwtSettings();

        ValidateSigningKey(settings);

        builder.Services.Configure<JwtSettings>(
            builder.Configuration.GetSection(JwtSettings.SectionName));

        builder.Services
            .AddIdentityCore<AppUser>(options =>
            {
                // Phone is the login handle in this market, so the username is
                // an E.164 number and the '+' has to be a legal character.
                options.User.RequireUniqueEmail = false;
                options.User.AllowedUserNameCharacters = "+0123456789";

                // Eight characters, no symbol or uppercase requirement. Rules
                // that fight the user produce "Password1!" written on a sticky
                // note; length plus lockout is the pair that actually helps.
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireDigit = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.SignIn.RequireConfirmedPhoneNumber = false;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<DataContext>()
            .AddDefaultTokenProviders();

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(settings.SigningKey)),
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateLifetime = true,

                    // The default is five minutes, which keeps a revoked token
                    // working for five minutes after it expires.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        // Lets the Angular interceptor tell "expired, refresh
                        // silently" apart from "invalid, send them to login".
                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            context.Response.Headers["X-Token-Expired"] = "true";
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.RequireAdmin, policy =>
                policy.RequireRole(Roles.Admin))
            .AddPolicy(Policies.RequireAdminOrManager, policy =>
                policy.RequireRole(Roles.Admin, Roles.Manager))
            .AddPolicy(Policies.RequireStaff, policy =>
                policy.RequireRole(Roles.Admin, Roles.Manager, Roles.Staff))
            .AddPolicy(Policies.RequireCustomer, policy =>
                policy.RequireRole(Roles.Customer));

        return builder;
    }

    /// <summary>
    /// Fails startup rather than booting with a weak key.
    /// </summary>
    /// <remarks>
    /// Internal so it can be tested without booting the host. A short HMAC key
    /// is brute-forceable, and a forged token here is an Admin token — refusing
    /// to start is the correct response, and it is much easier to notice than a
    /// warning in a log nobody reads.
    /// </remarks>
    internal static void ValidateSigningKey(JwtSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SigningKey) || settings.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 characters. Set it with "
                + "`dotnet user-secrets set \"Jwt:SigningKey\" \"<value>\"` locally, "
                + "or the Jwt__SigningKey environment variable in production.");
        }
    }
}
