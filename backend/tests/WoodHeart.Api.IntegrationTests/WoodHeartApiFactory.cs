using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WoodHeart.Api.IntegrationTests;

/// <summary>
/// Boots the real API — real pipeline, real DI, real middleware order — in memory.
/// </summary>
/// <remarks>
/// <para>
/// The point of testing through <see cref="WebApplicationFactory{TEntryPoint}"/>
/// rather than instantiating a controller is that most of what can break lives
/// <em>between</em> the controller and the handler: the validation behaviour,
/// the transaction boundary, the exception handler, JSON casing, and the order
/// of CORS and auth. A unit test on a controller method proves none of that.
/// </para>
/// <para>
/// Settings go through <see cref="IWebHostBuilder.UseSetting"/> rather than
/// <c>ConfigureAppConfiguration</c>. Under the minimal hosting model,
/// <c>Program.cs</c> reads configuration while registering services — which
/// happens before deferred configuration sources are applied — so a signing key
/// added that way arrives too late and startup fails. <c>UseSetting</c> writes
/// into host configuration, which is in place before the first line of
/// <c>Program.cs</c> runs.
/// </para>
/// </remarks>
public sealed class WoodHeartApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Obviously fake, and long enough to satisfy the startup guard rather than
    /// bypass it — the tests exercise the same validation production does.
    /// </summary>
    private const string TestSigningKey = "integration-tests-only-signing-key-not-a-secret";

    private const string FallbackConnection =
        "Host=localhost;Port=5432;Database=woodheart_test;Username=woodheart;Password=woodheart";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("Jwt:Issuer", "woodheart-api");
        builder.UseSetting("Jwt:Audience", "woodheart-web");

        // CI points this at the workflow's PostgreSQL service. Tests that never
        // touch the database never open a connection, so the fallback being
        // unreachable locally is harmless for them.
        builder.UseSetting(
            "ConnectionStrings:Default",
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? FallbackConnection);

        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");
    }

    /// <summary>True when CI has supplied a real database, so DB-dependent tests can skip cleanly.</summary>
    public static bool DatabaseAvailable =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Default"));
}
