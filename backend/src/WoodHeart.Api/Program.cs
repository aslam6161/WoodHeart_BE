using System.Reflection;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using Serilog;
using WoodHeart.Api.Extensions;
using WoodHeart.Api.Middleware;
using WoodHeart.Application;
using WoodHeart.Infrastructure;

// -----------------------------------------------------------------------------
// Bootstrap logger: active before the host is built, so a configuration mistake
// that stops startup is still written somewhere we can read it.
// -----------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("WoodHeart API starting up");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithProperty("Application", "WoodHeart.Api"));

    // -------------------------------------------------------------------------
    // Composition root. Each layer registers only what it owns, and the order
    // reflects the onion: inner layers know nothing about the ones after them.
    // -------------------------------------------------------------------------
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);
    builder.Services.AddWoodHeartAuthentication(builder.Configuration);

    var app = builder.Build();

    // -------------------------------------------------------------------------
    // Pipeline. Order matters far more than it looks:
    //   forwarded headers  → so rate limiting sees the real client IP
    //   correlation id     → so everything after it can log with the id
    //   exception handler  → so it wraps every stage below
    //   CORS               → before auth, or a preflight gets a 401
    //   auth               → before rate limiting, so limits partition per user
    // -------------------------------------------------------------------------
    app.UseForwardedHeaders();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseExceptionHandler();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0}ms";

        options.EnrichDiagnosticContext = (diagnostic, context) =>
        {
            diagnostic.Set("CorrelationId", context.TraceIdentifier);
            diagnostic.Set("ClientIp", context.Connection.RemoteIpAddress?.ToString());

            if (context.User.Identity?.IsAuthenticated == true)
            {
                diagnostic.Set("User", context.User.Identity.Name);
            }
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("WoodHeart API")
            .WithTheme(ScalarTheme.BluePlanet)
            .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Fetch));
    }
    else
    {
        // Only in production: in development the API is served over plain HTTP
        // and a redirect would break the Angular dev proxy.
        app.UseHttpsRedirection();
        app.UseHsts();
    }

    app.UseCors(ApiServiceExtensions.CorsPolicy);
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.MapControllers();

    // Liveness: is the process up? Must not touch the database — a failing
    // dependency should not cause the orchestrator to kill a healthy process.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    // Readiness: can this instance actually serve traffic? Checks dependencies.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    Log.Information("WoodHeart API ready in {Environment}", app.Environment.EnvironmentName);

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "WoodHeart API failed to start");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> in the integration
/// tests can boot the real pipeline rather than a hand-built approximation of it.
/// </summary>
public partial class Program;
