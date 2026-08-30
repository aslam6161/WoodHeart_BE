using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Helpers;
using WoodHeart.Presentation.Middleware;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Repository.Interfaces.Identity;
using WoodHeart.Repository.Repositories.Common;
using WoodHeart.Repository.Repositories.Identity;
using WoodHeart.Service.Infrastructure.Correlation;
using WoodHeart.Service.Infrastructure.Security;
using WoodHeart.Service.Infrastructure.Time;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Common;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Presentation.Extensions;

/// <summary>
/// Every DI registration in the application, in one place.
/// </summary>
/// <remarks>
/// One long file rather than a registration scattered across each module. It is
/// unglamorous, but it means the answer to "what is this interface bound to?"
/// is always found by searching one file, and a duplicate or missing
/// registration is visible rather than buried.
/// </remarks>
public static class ApplicationServiceExtensions
{
    public static WebApplicationBuilder AddApplicationService(this WebApplicationBuilder builder)
    {
        builder.Services.AddDataAccess(builder.Configuration);
        builder.Services.AddSystemServices();
        builder.Services.AddBusinessServices();
        builder.Services.AddWebServices();

        return builder;
    }

    // -------------------------------------------------------------------------

    private static IServiceCollection AddDataAccess(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<DataContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history");

                // Bangladeshi hosting drops connections more often than a
                // European datacentre would. Retrying transient faults here is
                // the difference between a blip and a failed checkout.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            })
            // Produces order_items rather than OrderItems, for the whole schema
            // including the Identity tables.
            .UseSnakeCaseNamingConvention());

        // The context is the unit of work: same instance, same transaction.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DataContext>());

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // Common
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IStoreSettingRepository, StoreSettingRepository>();
        services.AddScoped<IFeatureFlagRepository, FeatureFlagRepository>();

        // Identity
        services.AddScoped<IUserRefreshTokenRepository, UserRefreshTokenRepository>();

        return services;
    }

    private static IServiceCollection AddSystemServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddDataProtection();

        // Stateless and thread-safe, and the timezone lookup it does on first
        // use is worth doing once rather than per request.
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICorrelationContext, CorrelationContext>();

        services.AddSingleton<ITokenHasher, TokenHasher>();
        services.AddSingleton<ISecretProtector, SecretProtector>();

        // Singletons that need a repository resolve their own scope per read —
        // see StoreSettingService for why these are cached rather than scoped.
        services.AddSingleton<IStoreSettingService, StoreSettingService>();
        services.AddSingleton<IFeatureFlagService, FeatureFlagService>();

        return services;
    }

    private static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        services.AddScoped<IDiagnosticsService, DiagnosticsService>();
        services.AddScoped<INotificationQueue, NotificationQueue>();

        return services;
    }

    private static IServiceCollection AddWebServices(this IServiceCollection services)
    {
        services.AddControllers(options =>
            {
                // Runs before every action, so a binding failure comes back in
                // the same shape as a business failure.
                options.Filters.Add<ValidationFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy =
                    System.Text.Json.JsonNamingPolicy.CamelCase;

                // Enums cross the wire as names. An Angular client comparing
                // against "AwaitingCollection" keeps working when someone
                // inserts a new enum member and shifts every number after it.
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

                options.JsonSerializerOptions.DefaultIgnoreCondition =
                    JsonIgnoreCondition.WhenWritingNull;
            });

        // Suppressed because ValidationFilter above owns the response shape.
        // Without this, ASP.NET short-circuits first and returns its own.
        services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        return services;
    }
}
