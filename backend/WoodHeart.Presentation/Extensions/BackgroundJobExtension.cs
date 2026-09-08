using Hangfire;
using Hangfire.PostgreSql;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Settings;
using WoodHeart.Service.Interfaces.Notifications;

namespace WoodHeart.Presentation.Extensions;

/// <summary>
/// Hangfire: the server, the schedule, and the dashboard.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same PostgreSQL database, in its own schema.</b> A separate store
/// would be one more thing to provision, back up and keep alive for a shop this
/// size; the schema keeps Hangfire's tables from mixing with the shop's, so
/// <c>\dt</c> still shows something a person can read and a migration never
/// meets a job table.
/// </para>
/// <para>
/// <b>The API process is also the worker</b>, unless
/// <c>BackgroundJobs:Enabled</c> says otherwise. One deployable, one connection
/// pool, one set of logs. The trade is that a queue backlog competes with HTTP
/// requests for threads — worth accepting until the shop has enough traffic to
/// justify a second container, and that switch is what makes the split a
/// configuration change rather than a rewrite.
/// </para>
/// <para>
/// <b>Nothing here is registered when the switch is off.</b> Not the server,
/// not the storage, not the dashboard — because <c>UsePostgreSqlStorage</c>
/// prepares its schema on first use, and a process that is not running jobs
/// should not be reaching for a database to create tables it will never read.
/// </para>
/// </remarks>
public static class BackgroundJobExtension
{
    /// <summary>How often the outbox is drained.</summary>
    /// <remarks>
    /// Every minute. A customer who has just placed an order expects the
    /// confirmation while they are still looking at the page, and the query
    /// behind an empty queue is one indexed lookup that returns nothing.
    /// </remarks>
    private const string OutboxSchedule = "* * * * *";

    private static BackgroundJobSettings Read(IConfiguration configuration) =>
        configuration.GetSection(BackgroundJobSettings.SectionName).Get<BackgroundJobSettings>()
        ?? new BackgroundJobSettings();

    public static WebApplicationBuilder AddBackgroundJobs(this WebApplicationBuilder builder)
    {
        var settings = Read(builder.Configuration);

        builder.Services.Configure<BackgroundJobSettings>(
            builder.Configuration.GetSection(BackgroundJobSettings.SectionName));

        if (!settings.Enabled)
        {
            return builder;
        }

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

        builder.Services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",

                    // Long enough that a job which merely takes a while is not
                    // declared dead and run a second time, short enough that a
                    // container killed mid-job is picked up within the hour.
                    InvisibilityTimeout = TimeSpan.FromMinutes(30),
                    QueuePollInterval = TimeSpan.FromSeconds(15),
                    PrepareSchemaIfNecessary = true
                }));

        builder.Services.AddHangfireServer(options =>
        {
            options.ServerName = $"woodheart-{Environment.MachineName}";
            options.WorkerCount = settings.WorkerCount;
        });

        return builder;
    }

    /// <summary>
    /// Registers the schedule and mounts the dashboard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The dashboard is behind the admin policy.</b> Hangfire's default
    /// authorization filter allows any local request and, behind a reverse
    /// proxy, every request looks local — so the default would publish the
    /// payload of every notification, including customers' names, phone numbers
    /// and addresses, to anyone who found the URL.
    /// </para>
    /// <para>
    /// It sits outside <c>/api</c> so the storefront's CORS policy and rate
    /// limiters do not apply to it. It is a staff tool reached from a browser,
    /// not part of the API surface.
    /// </para>
    /// </remarks>
    public static WebApplication UseBackgroundJobs(this WebApplication app)
    {
        if (!Read(app.Configuration).Enabled)
        {
            return app;
        }

        app.MapHangfireDashboard("/jobs", new DashboardOptions
        {
            DashboardTitle = "WoodHeart jobs",

            // Read-only outside Development. Watching a queue is a diagnostic;
            // requeuing and deleting jobs from a web page is how a duplicate
            // SMS run gets triggered by somebody exploring.
            IsReadOnlyFunc = _ => !app.Environment.IsDevelopment()
        })
        .RequireAuthorization(Policies.RequireAdmin);

        // Registered by interface, so Hangfire resolves the implementation from
        // DI at run time rather than baking a concrete type name into the job
        // table — which would strand every scheduled job the day the class is
        // renamed.
        RecurringJob.AddOrUpdate<IOutboxDispatcher>(
            recurringJobId: "outbox-dispatch",
            methodCall: dispatcher => dispatcher.RunAsync(CancellationToken.None),
            cronExpression: OutboxSchedule,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        return app;
    }
}
