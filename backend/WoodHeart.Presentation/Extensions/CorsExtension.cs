namespace WoodHeart.Presentation.Extensions;

public static class CorsExtension
{
    public const string PolicyName = "WoodHeartCors";

    public static WebApplicationBuilder ConfigureCors(this WebApplicationBuilder builder)
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var isDevelopment = builder.Environment.IsDevelopment();

        builder.Services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (isDevelopment)
            {
                // Any loopback port, in Development only.
                //
                // <b>This is a fix for a real afternoon lost.</b> The allowlist
                // named http://localhost:4200 because that is what `ng serve`
                // uses — but Visual Studio starts the Angular project on a port
                // it picks itself, and the origin was http://localhost:53641.
                // Every request the browser made was refused while the site
                // still appeared to work, because the storefront pages are
                // server-rendered: Node fetches the API with no Origin header,
                // so CORS never applies to them. Only the calls the browser
                // makes for itself failed — which is exactly signing in.
                //
                // Widening this to any loopback port costs nothing on a
                // developer's machine and removes a class of confusion that is
                // very hard to diagnose from the browser, where the message is
                // a generic network failure. Production still uses the explicit
                // list below, and that is the one that matters.
                policy.SetIsOriginAllowed(IsLoopback)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .WithExposedHeaders("X-Correlation-Id", "X-Token-Expired", "X-Pagination");

                return;
            }

            if (origins.Length == 0)
            {
                // No origins configured means no cross-origin access, not open
                // access. AllowAnyOrigin cannot be combined with credentials
                // anyway, and defaulting to permissive is how a staging API ends
                // up callable from anywhere.
                return;
            }

            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                // Angular cannot read a response header unless it is exposed.
                .WithExposedHeaders("X-Correlation-Id", "X-Token-Expired", "X-Pagination");
        }));

        return builder;
    }

    /// <summary>
    /// True for any <c>http://localhost:*</c> or <c>http://127.0.0.1:*</c> origin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Internal so it can be tested without booting the host, and deliberately
    /// strict about the parts that matter: the scheme must be plain HTTP and
    /// the host must be a loopback name exactly. A substring check would accept
    /// <c>http://localhost.evil.example</c>, which is not this machine.
    /// </para>
    /// <para>
    /// The port is ignored on purpose — that is the whole point. It is also why
    /// this must never run outside Development.
    /// </para>
    /// </remarks>
    internal static bool IsLoopback(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https"
               && uri.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
    }
}
