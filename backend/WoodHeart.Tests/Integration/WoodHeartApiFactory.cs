using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// Boots the real API in memory.
/// </summary>
/// <remarks>
/// <para>
/// Configuration is supplied through <see cref="IWebHostBuilder.UseSetting"/>,
/// not <c>ConfigureAppConfiguration</c>. Under the minimal hosting model
/// <c>Program.cs</c> reads configuration while registering services, which
/// happens before <c>ConfigureAppConfiguration</c> callbacks run — so the
/// signing-key check would fail and every test would die in the constructor.
/// <c>UseSetting</c> writes host configuration, which is available before the
/// first line of <c>Program.cs</c> executes.
/// </para>
/// <para>
/// Seeding is switched off: these tests exercise the pipeline — routing,
/// validation, error shape, correlation — none of which needs a database.
/// Anything that genuinely needs Postgres belongs in a Testcontainers-backed
/// fixture instead.
/// </para>
/// </remarks>
public class WoodHeartApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");

        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            "Host=localhost;Port=5432;Database=woodheart_test;Username=woodheart;Password=woodheart");

        // 32+ characters, as the startup guard requires. Test-only value.
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789");
        builder.UseSetting("Jwt:Issuer", "WoodHeart");
        builder.UseSetting("Jwt:Audience", "WoodHeart.Client");

        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");

        builder.UseSetting("Seed:Enabled", "false");
    }
}
