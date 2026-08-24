using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WoodHeart.Application.Common.Abstractions;
using WoodHeart.Infrastructure.Identity;
using WoodHeart.Infrastructure.Messaging;
using WoodHeart.Infrastructure.Persistence;
using WoodHeart.Infrastructure.Services;

namespace WoodHeart.Infrastructure;

/// <summary>
/// Wires every adapter to the port it implements.
/// </summary>
/// <remarks>
/// This is the only place the outside world learns that <c>IUnitOfWork</c> is
/// EF Core, or that <c>ISmsSender</c> talks to a Bangladeshi SMS gateway.
/// Swapping any of them is a one-line change here.
/// </remarks>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddIdentityServices();
        services.AddSystemServices();

        return services;
    }

    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Set it in appsettings, " +
                "user-secrets, or the ConnectionStrings__Default environment variable.");

        services.AddDbContext<WoodHeartDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history");

                // Transient network faults are normal on a VPS; retry them rather
                // than turning a blip into a failed checkout.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });

            // Postgres convention: snake_case everywhere, including the tables
            // ASP.NET Identity creates for us.
            options.UseSnakeCaseNamingConvention();

            var environment = provider.GetService<IHostEnvironment>();

            if (environment?.IsDevelopment() == true)
            {
                // Never in production: parameter values include phone numbers
                // and addresses, which must not land in a log file.
                options.EnableDetailedErrors();
                options.EnableSensitiveDataLogging();
            }
        });

        // The context is the unit of work and the outbox writer — one instance
        // per request, so all three share the same change tracker.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<WoodHeartDbContext>());
        services.AddScoped<IOutbox>(sp => sp.GetRequiredService<WoodHeartDbContext>());

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }

    private static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Passwords: long beats complex. A 8+ character requirement with
                // no symbol gymnastics produces stronger real-world passwords
                // than rules that push people to "Pa$$w0rd1".
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.User.RequireUniqueEmail = false;

                // Email is optional in this market; the phone number is the
                // handle, so uniqueness is enforced on UserName instead.
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedPhoneNumber = false;

                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<WoodHeartDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    private static IServiceCollection AddSystemServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddSingleton<ITokenHasher, TokenHasher>();
        services.AddScoped<ISecretProtector, SecretProtector>();

        return services;
    }
}
