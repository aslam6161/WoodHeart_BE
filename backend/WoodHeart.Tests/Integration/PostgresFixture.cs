using Microsoft.EntityFrameworkCore;
using Npgsql;
using WoodHeart.Repository;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// A throwaway PostgreSQL database with the migrations applied.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why any test needs a real database at all.</b> Most of this suite runs
/// against substitutes, which is right — they are fast and they test our rules.
/// But a substitute has no unique indexes, no filtered indexes and no statement
/// ordering, so an entire class of defect is invisible to it. This project has
/// already shipped two: thirteen queries that compiled, passed 198 tests and
/// threw <c>InvalidCastException</c> on their first real SQL statement, and a
/// search predicate EF could not translate at all.
/// </para>
/// <para>
/// So this exists for the handful of rules that <i>are</i> the database:
/// <c>ux_product_media_one_primary</c> in particular.
/// </para>
/// <para>
/// <b>Skipped, not failed, when there is no database.</b> The connection string
/// comes from <c>WOODHEART_TEST_DB</c>. CI sets it from the Postgres service
/// that is already running for the migration step; a developer without Docker
/// installed simply does not run these, and <c>dotnet test</c> stays green on a
/// fresh checkout. A test that fails on every machine but the build server is a
/// test people learn to ignore.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string SkipReason =
        "Set WOODHEART_TEST_DB to a PostgreSQL connection string to run the database tests.";

    /// <summary>The connection string for the disposable database, or null.</summary>
    public string? ConnectionString { get; private set; }

    public static string? BaseConnectionString =>
        Environment.GetEnvironmentVariable("WOODHEART_TEST_DB");

    public static bool Available => !string.IsNullOrWhiteSpace(BaseConnectionString);

    private string? databaseName;

    public async Task InitializeAsync()
    {
        if (BaseConnectionString is not { } baseConnection)
        {
            return;
        }

        // A database per run, so a failed run never leaves rows that make the
        // next one pass or fail for the wrong reason.
        databaseName = $"woodheart_test_{Guid.NewGuid():n}";

        var builder = new NpgsqlConnectionStringBuilder(baseConnection);
        var maintenance = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres" };

        await using (var connection = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        builder.Database = databaseName;
        ConnectionString = builder.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>A fresh context on the disposable database.</summary>
    /// <remarks>
    /// New per call rather than shared, because the change tracker is what these
    /// tests are trying to see past: a rule that only holds because an entity
    /// was already in memory is not a rule the database enforces.
    /// </remarks>
    public DataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new DataContext(options, new FakeClock());
    }

    public async Task DisposeAsync()
    {
        if (databaseName is null || BaseConnectionString is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();

        var maintenance = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself when no database is configured.
/// </summary>
/// <remarks>
/// xUnit v2 decides skipping at discovery, so the check has to happen in the
/// attribute rather than inside the test body.
/// </remarks>
public sealed class RequiresPostgresFactAttribute : FactAttribute
{
    public RequiresPostgresFactAttribute()
    {
        if (!PostgresFixture.Available)
        {
            Skip = PostgresFixture.SkipReason;
        }
    }
}
