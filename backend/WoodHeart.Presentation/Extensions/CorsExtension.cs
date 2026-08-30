namespace WoodHeart.Presentation.Extensions;

public static class CorsExtension
{
    public const string PolicyName = "WoodHeartCors";

    public static WebApplicationBuilder ConfigureCors(this WebApplicationBuilder builder)
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        builder.Services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
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
}
