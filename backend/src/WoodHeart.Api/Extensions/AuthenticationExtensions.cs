using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using WoodHeart.Infrastructure.Identity;

namespace WoodHeart.Api.Extensions;

/// <summary>JWT bearer authentication and the authorization policies built on it.</summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddWoodHeartAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("The Jwt configuration section is missing.");

        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
        {
            // A short key makes the signature forgeable, which means anyone can
            // mint an Admin token. Failing at startup is the only safe response.
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 characters. Set it via user-secrets in " +
                "development and the Jwt__SigningKey environment variable in production.");
        }

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.SaveToken = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,

                    // Default is 5 minutes, which would keep a revoked 15-minute
                    // token alive a third longer than intended.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        // Lets the Angular interceptor tell "refresh me" apart
                        // from "your token is malformed, log in again".
                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            context.Response.Headers.Append("X-Token-Expired", "true");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, policy =>
                policy.RequireRole(Roles.Admin))
            .AddPolicy(Policies.AdminOrManager, policy =>
                policy.RequireRole(Roles.Admin, Roles.Manager))
            .AddPolicy(Policies.Staff, policy =>
                policy.RequireRole(Roles.Admin, Roles.Manager, Roles.Staff))
            .AddPolicy(Policies.CustomerOnly, policy =>
                policy.RequireRole(Roles.Customer));

        return services;
    }
}

/// <summary>Named authorization policies, so a typo fails to compile.</summary>
public static class Policies
{
    public const string AdminOnly = "admin-only";
    public const string AdminOrManager = "admin-or-manager";
    public const string Staff = "staff";
    public const string CustomerOnly = "customer-only";
}

/// <summary>Token settings, bound from configuration.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "woodheart-api";

    public string Audience { get; init; } = "woodheart-web";

    /// <summary>
    /// Never commit a real value. User-secrets in development, an environment
    /// variable in production.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Short-lived by design. A stolen access token is useful for minutes, not
    /// weeks, and the refresh token is what carries the long session.
    /// </summary>
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>Rotated on every use, so a stolen one is good for a single request.</summary>
    public int RefreshTokenDays { get; init; } = 14;
}
