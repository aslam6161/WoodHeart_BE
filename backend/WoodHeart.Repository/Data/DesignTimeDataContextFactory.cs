using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Helpers;

namespace WoodHeart.Repository.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build a context without booting the API.
/// </summary>
/// <remarks>
/// <para>
/// Reads <c>WOODHEART_MIGRATIONS_CONNECTION</c>, falling back to the local
/// docker-compose database. It never reads the API's appsettings, which is
/// deliberate: generating a migration must not be able to touch production
/// because someone had the wrong environment variable set in their shell.
/// </para>
/// <para>
/// Run migrations with <c>--project WoodHeart.Repository</c> and the same
/// project as the startup project.
/// </para>
/// </remarks>
public class DesignTimeDataContextFactory : IDesignTimeDbContextFactory<DataContext>
{
    private const string LocalDevelopmentConnection =
        "Host=localhost;Port=5432;Database=woodheart;Username=woodheart;Password=woodheart";

    public DataContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("WOODHEART_MIGRATIONS_CONNECTION")
            ?? LocalDevelopmentConnection;

        var options = new DbContextOptionsBuilder<DataContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new DataContext(options, new DesignTimeClock());
    }

    /// <summary>
    /// A clock for design time only. Migrations never save entities, so nothing
    /// here is ever stamped — this exists purely to satisfy the constructor.
    /// </summary>
    private sealed class DesignTimeClock : IDateTimeProvider
    {
        private static readonly TimeSpan DhakaOffset = TimeSpan.FromHours(6);

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public DateTimeOffset DhakaNow => UtcNow.ToOffset(DhakaOffset);

        public DateOnly DhakaToday => DateOnly.FromDateTime(DhakaNow.DateTime);

        public DateTimeOffset DhakaToUtc(DateOnly date, TimeOnly time) =>
            new(date.ToDateTime(time), DhakaOffset);

        public DateTimeOffset ToDhaka(DateTimeOffset utc) => utc.ToOffset(DhakaOffset);
    }
}

/// <summary>
/// Marker for assembly scanning, so callers never guess at a string name.
/// </summary>
public static class RepositoryAssembly
{
    public static readonly System.Reflection.Assembly Reference =
        typeof(RepositoryAssembly).Assembly;

    /// <summary>The timezone this application reports in. See <see cref="GlobalConstants"/>.</summary>
    public const string TimeZoneId = GlobalConstants.TimeZoneId;
}
