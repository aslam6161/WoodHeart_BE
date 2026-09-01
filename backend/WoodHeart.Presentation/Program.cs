using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Scalar.AspNetCore;
using Serilog;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Helpers;
using WoodHeart.Presentation.Extensions;
using WoodHeart.Presentation.Logging;
using WoodHeart.Presentation.Middleware;
using WoodHeart.Repository;
using WoodHeart.Repository.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.With(new CorrelationIdEnricher(
        services.GetRequiredService<IHttpContextAccessor>())));

builder.Services.AddWoodHeartOpenApi();

builder.AddApplicationService();
builder.AddIdentityService();
builder.ConfigureCors();
builder.ConfigureRateLimiting();

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgres",
        tags: ["ready"]);

// The API sits behind nginx in production, so without this every request looks
// like it came from the proxy — which would break rate limiting and put the
// wrong IP in the audit trail.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseCors(CorsExtension.PolicyName);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Liveness must not touch the database: a database outage should page someone,
// not make the orchestrator kill an otherwise healthy container.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

await SeedDatabaseAsync(app);

await app.RunAsync();

return;

static async Task SeedDatabaseAsync(WebApplication app)
{
    // Off in the integration tests, which exercise the pipeline without a
    // database. Everywhere else this runs, and is idempotent, so it is safe on
    // every start. Migrations are NOT applied here on purpose — an app that
    // migrates itself on boot will happily apply a half-reviewed schema change
    // to production during a rolling deploy.
    if (!app.Configuration.GetValue("Seed:Enabled", true))
    {
        return;
    }

    using var scope = app.Services.CreateScope();

    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        var context = services.GetRequiredService<DataContext>();
        var roleManager = services.GetRequiredService<RoleManager<AppRole>>();

        await Seed.RunAsync(context, roleManager);

        // Gated separately, and defaulting to OFF. Roles and settings belong in
        // every environment; a dozen sample sofas do not. Turning this on in
        // production would put placeholder prices in front of customers.
        if (app.Configuration.GetValue("Seed:Catalog", false))
        {
            var clock = services.GetRequiredService<IDateTimeProvider>();

            await CatalogSeed.RunAsync(context, clock);
        }
    }
    catch (Exception exception)
    {
        StartupLog.SeedFailed(logger, exception);
        throw;
    }
}

/// <summary>Exposed so the integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
