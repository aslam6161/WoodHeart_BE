using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WoodHeart.Infrastructure.Services;

namespace WoodHeart.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build a context without starting the API.
/// </summary>
/// <remarks>
/// <para>
/// The design-time connection string never needs to point at a real database
/// with data in it — EF only reads the model to generate the migration. It does
/// have to be a valid Npgsql connection string, and it must use the same
/// provider and conventions as production, or the generated SQL will not match
/// what actually runs.
/// </para>
/// <para>
/// Override it locally with <c>WOODHEART_MIGRATIONS_CONNECTION</c> when
/// scaffolding against a real database.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WoodHeartDbContext>
{
    private const string FallbackConnection =
        "Host=localhost;Port=5432;Database=woodheart;Username=woodheart;Password=woodheart";

    public WoodHeartDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("WOODHEART_MIGRATIONS_CONNECTION")
                         ?? FallbackConnection;

        var options = new DbContextOptionsBuilder<WoodHeartDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;

        // No HTTP request exists at design time, so the ambient-context services
        // are replaced with inert stand-ins.
        return new WoodHeartDbContext(
            options,
            new DateTimeProvider(),
            new SystemUser(),
            new DesignTimeCorrelationContext());
    }

    private sealed class DesignTimeCorrelationContext : Application.Common.Abstractions.ICorrelationContext
    {
        public string CorrelationId => "design-time";
    }
}
