namespace WoodHeart.Domain.Settings;

/// <summary>
/// How hard, and how often, the outbox worker tries.
/// </summary>
/// <remarks>
/// <para>
/// The numbers here are not arbitrary. SMS in Bangladesh is billed per message
/// part and a Bangla message costs a part every 70 characters, so a retry loop
/// that runs away is a line on an invoice rather than noise in a log. The
/// backoff is exponential and the attempt count is capped for that reason,
/// not for tidiness.
/// </para>
/// </remarks>
public class OutboxSettings
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// How many messages one pass claims.
    /// </summary>
    /// <remarks>
    /// Small on purpose. The batch is held under <c>FOR UPDATE SKIP LOCKED</c>
    /// for as long as its slowest gateway call, and a large batch turns one
    /// slow provider into a long-held lock on rows other workers could have
    /// taken.
    /// </remarks>
    public int BatchSize { get; set; } = 25;

    /// <summary>
    /// Attempts before a message is given up on and left for a person.
    /// </summary>
    /// <remarks>
    /// Five, with the backoff below, spans about half an hour — long enough to
    /// ride out a gateway restart and short enough that a genuinely broken
    /// message reaches the dashboard the same morning.
    /// </remarks>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The first retry delay. Each subsequent attempt doubles it.</summary>
    public int RetryBaseSeconds { get; set; } = 60;

    /// <summary>The ceiling on that doubling, so attempt ten is not next week.</summary>
    public int RetryMaxSeconds { get; set; } = 3600;
}

/// <summary>
/// The SMS gateway.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ApiKey"/> never appears in <c>appsettings.json</c>.</b> It
/// ships empty and is supplied through user-secrets locally and an environment
/// variable in production, the same way <c>Jwt:SigningKey</c> is. Anyone
/// holding it can spend the shop's SMS balance.
/// </para>
/// <para>
/// <b>An empty key is not a misconfiguration.</b> It selects the logging sender
/// instead, which writes the message it would have sent and reports success.
/// That is what makes a developer machine, and a fresh clone, work without
/// credentials and without silently swallowing every notification — the log
/// line is the evidence.
/// </para>
/// </remarks>
public class SmsSettings
{
    public const string SectionName = "Sms";

    /// <summary>Alpha SMS (api.sms.net.bd). Left as a setting because it is one line to change.</summary>
    public string BaseUrl { get; set; } = "https://api.sms.net.bd";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The registered sender id, where the gateway requires one.</summary>
    public string? SenderId { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>True when a gateway is actually configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// The outgoing mail server.
/// </summary>
/// <remarks>
/// Email is the secondary channel here and always optional: many customers in
/// this market have no email address they read, which is why
/// <c>Order.ContactEmail</c> is nullable and why an order with no email is not
/// a failed notification.
/// </remarks>
public class EmailSettings
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    /// <summary>Never in <c>appsettings.json</c>. User-secrets locally, environment variable in production.</summary>
    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "WoodHeart";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>True when a mail server is actually configured.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>
/// Whether this process runs background jobs.
/// </summary>
/// <remarks>
/// <para>
/// <b>A real switch, not a test affordance.</b> The API process is also the
/// worker today, which is the right trade for a shop this size — one
/// deployable, one connection pool, one set of logs. When the queue eventually
/// competes with HTTP requests for threads, the fix is to run a second
/// container with <c>BackgroundJobs__Enabled=true</c> and turn it off on the
/// web instances. That is the whole of the change.
/// </para>
/// <para>
/// It also means a process with no reachable job storage still boots. The
/// integration tests are the immediate case, but so is anyone running the API
/// against a database that has not had the Hangfire schema created yet.
/// </para>
/// </remarks>
public class BackgroundJobSettings
{
    public const string SectionName = "BackgroundJobs";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Workers per process.
    /// </summary>
    /// <remarks>
    /// Small on purpose. These jobs are almost entirely waiting on a gateway or
    /// the database, and while the API and the worker share a process, serving
    /// HTTP requests comes first.
    /// </remarks>
    public int WorkerCount { get; set; } = 4;
}
