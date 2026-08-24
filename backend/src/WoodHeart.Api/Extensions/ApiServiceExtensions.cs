using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Api.Middleware;

namespace WoodHeart.Api.Extensions;

/// <summary>Registers the web-facing concerns: JSON, CORS, rate limits, docs, health.</summary>
public static class ApiServiceExtensions
{
    public const string CorsPolicy = "woodheart-web";

    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                // camelCase matches what the Angular client expects; enums travel
                // as strings so a reordered enum cannot silently change meaning
                // in a stored payload or an API consumer.
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition =
                    JsonIgnoreCondition.WhenWritingNull;
            });

        // The pipeline's ValidationBehaviour owns validation and returns a
        // consistent problem shape. The MVC filter would short-circuit first
        // with a different shape, so it is switched off.
        services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        services.AddHttpContextAccessor();
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddOpenApi();
        services.AddApiCors(configuration);
        services.AddApiRateLimiting();
        services.AddApiHealthChecks(configuration);

        // The API sits behind Nginx and Cloudflare in production, so the real
        // client IP arrives in a header. Without this, rate limiting would see
        // every request as coming from the proxy and throttle all customers as one.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    private static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            if (origins.Length == 0)
            {
                // Deliberately not AllowAnyOrigin: a wildcard here would let any
                // site on the internet drive this API with a logged-in user's
                // credentials. An empty list means "nothing configured yet".
                return;
            }

            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders("X-Correlation-Id", "X-Token-Expired", "X-Pagination");
        }));

        return services;
    }

    /// <summary>
    /// Rate limits, tightest on the endpoints that cost money or leak information.
    /// </summary>
    /// <remarks>
    /// Three tiers rather than one global limit, because the right ceiling for
    /// browsing a category page and for attempting a login differ by two orders
    /// of magnitude. Partitioning is per authenticated user where possible and
    /// per IP otherwise, so one abusive visitor cannot throttle everyone else.
    /// </remarks>
    private static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();
                }

                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "common.rate_limited",
                    title = "Too many requests",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "Please slow down and try again shortly.",
                    correlationId = context.HttpContext.TraceIdentifier
                }, token);
            };

            // Browsing: generous. A customer scrolling a category with images
            // legitimately fires a lot of requests.
            options.AddPolicy(RateLimitPolicies.Public, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // Auth, OTP, coupon validation: strict. These are the endpoints worth
            // brute-forcing — credentials, SMS spend, and discount-code guessing.
            options.AddPolicy(RateLimitPolicies.Sensitive, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // Checkout and payment: moderate, and queued rather than rejected —
            // failing a customer's genuine second attempt to pay is far worse
            // than making them wait a beat.
            options.AddPolicy(RateLimitPolicies.Checkout, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5
                    }));
        });

        return services;
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? $"user:{context.User.Identity.Name}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    private static IServiceCollection AddApiHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");

        var builder = services.AddHealthChecks();

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.AddNpgSql(
                connectionString,
                name: "postgres",
                tags: ["ready", "db"]);
        }

        return services;
    }
}

/// <summary>Rate-limit policy names.</summary>
public static class RateLimitPolicies
{
    public const string Public = "public";
    public const string Sensitive = "sensitive";
    public const string Checkout = "checkout";
}
