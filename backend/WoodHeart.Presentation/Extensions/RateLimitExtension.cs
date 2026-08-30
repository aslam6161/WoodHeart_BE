using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;

namespace WoodHeart.Presentation.Extensions;

public static class RateLimitExtension
{
    public static WebApplicationBuilder ConfigureRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Browsing. Generous — a customer scrolling a category page fires
            // several requests a second and must never be throttled for it.
            options.AddPolicy(RateLimitPolicies.Public, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveClientIp(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            // Login, OTP, password reset. Tight, because these are the
            // brute-force targets and OTP costs money per attempt.
            options.AddPolicy(RateLimitPolicies.Sensitive, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveClientIp(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            // Checkout and payment callbacks. Queued rather than rejected: a
            // customer who double-taps "Place order" on a slow 4G connection
            // should wait, not see an error on the one screen that matters.
            options.AddPolicy(RateLimitPolicies.Checkout, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveClientIp(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        isSuccess = false,
                        errorCode = "common.rate_limited",
                        message = "Too many requests. Please wait a moment and try again."
                    },
                    cancellationToken);
            };
        });

        return builder;
    }

    /// <summary>
    /// The client's IP, preferring the forwarded header.
    /// </summary>
    /// <remarks>
    /// Internal so it can be unit tested. Behind nginx or Cloudflare every
    /// request appears to come from the proxy, so partitioning on the raw
    /// connection IP would put every customer in the world into one bucket and
    /// rate-limit the whole store at once.
    /// </remarks>
    internal static string ResolveClientIp(HttpContext context)
    {
        // ForwardedHeaders middleware has already rewritten RemoteIpAddress when
        // the proxy is a configured known network; this is the fallback.
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            // Leftmost entry is the original client.
            var first = forwarded.Split(',', StringSplitOptions.TrimEntries)[0];

            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
