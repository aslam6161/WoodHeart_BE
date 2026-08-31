using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using WoodHeart.Domain.Entity;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Helpers;

namespace WoodHeart.Repository;

/// <summary>
/// The single EF Core context for the application.
/// </summary>
/// <remarks>
/// <para>
/// One context, not one per module. Separate contexts would forbid the very
/// thing a monolith exists to provide — a single transaction spanning an order
/// and its stock drawdown — while buying only cosmetic separation.
/// </para>
/// <para>
/// Entity configurations live in <c>Configurations/{Module}/</c> and are picked
/// up by assembly scan, so this file never grows into a 2,000-line
/// <c>OnModelCreating</c>.
/// </para>
/// </remarks>
public class DataContext(
    DbContextOptions<DataContext> options,
    IDateTimeProvider clock,
    IHttpContextAccessor? httpContextAccessor = null)
    : IdentityDbContext<AppUser, AppRole, long,
        IdentityUserClaim<long>, AppUserRole, IdentityUserLogin<long>,
        IdentityRoleClaim<long>, IdentityUserToken<long>>(options), IUnitOfWork
{
    private readonly IHttpContextAccessor? _httpContextAccessor = httpContextAccessor;

    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<StoreSetting> StoreSettings => Set<StoreSetting>();

    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    // --- Catalog ------------------------------------------------------------

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();

    public DbSet<Collection> Collections => Set<Collection>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        RenameIdentityTables(builder);
        ApplySoftDeleteQueryFilters(builder);
        ApplyConcurrencyTokens(builder);
        ApplyDecimalPrecision(builder);
    }

    // -------------------------------------------------------------------------
    // IUnitOfWork
    // -------------------------------------------------------------------------

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        ConvertDeletesToSoftDeletes();

        return await base.SaveChangesAsync(cancellationToken);
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        SaveChangesAsync(cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        // Already inside a transaction — a service calling another service.
        // Join it rather than nesting, so the whole use case commits atomically.
        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using IDbContextTransaction transaction = await Database.BeginTransactionAsync(ct);

            try
            {
                var result = await operation(ct);
                await transaction.CommitAsync(ct);

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }, cancellationToken);
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) =>
        await ExecuteInTransactionAsync<object?>(async ct =>
        {
            await operation(ct);
            return null;
        }, cancellationToken);

    // -------------------------------------------------------------------------
    // Save-time behaviour
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fills in <c>CreatedAt</c>/<c>CreatedBy</c>/<c>UpdatedAt</c>/<c>UpdatedBy</c>.
    /// </summary>
    /// <remarks>
    /// Centralised here for two reasons. The obvious one is that no service can
    /// forget. The one that matters more: this is the only place that holds both
    /// the injected clock and the request's user, which is what makes
    /// time-dependent behaviour testable — see <see cref="IDateTimeProvider"/>.
    /// </remarks>
    private void StampAuditFields()
    {
        var now = clock.UtcNow;
        var actor = CurrentUserId();

        foreach (var entry in ChangeTracker.Entries<IBaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = actor;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;
                    break;
            }
        }
    }

    /// <summary>
    /// Turns a hard delete into a soft one for anything marked
    /// <see cref="ISoftDeletable"/>.
    /// </summary>
    /// <remarks>
    /// Intercepting here rather than trusting every caller is what makes the
    /// guarantee real. An order line pointing at a vanished product makes
    /// historic invoices unprintable, and "delete this product" nearly always
    /// means "stop offering it".
    /// </remarks>
    private void ConvertDeletesToSoftDeletes()
    {
        var now = clock.UtcNow;
        var actor = CurrentUserId();

        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = now;
            entry.Entity.DeletedBy = actor;
        }
    }

    /// <summary>The signed-in user's id, or null for anonymous and background work.</summary>
    private long? CurrentUserId()
    {
        var value = _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        return long.TryParse(value, out var id) ? id : null;
    }

    // -------------------------------------------------------------------------
    // Model conventions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gives the ASP.NET Identity tables the same snake_case names as the rest
    /// of the schema, so the database reads as one design rather than two.
    /// </summary>
    private static void RenameIdentityTables(ModelBuilder builder)
    {
        builder.Entity<AppUser>().ToTable("users");
        builder.Entity<AppRole>().ToTable("roles");
        builder.Entity<AppUserRole>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<long>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<long>>().ToTable("user_logins");
        builder.Entity<IdentityRoleClaim<long>>().ToTable("role_claims");
        builder.Entity<IdentityUserToken<long>>().ToTable("user_tokens");

        // Explicit join navigations, so "staff and their roles" is one query.
        builder.Entity<AppUser>()
            .HasMany(u => u.UserRoles)
            .WithOne(ur => ur.User)
            .HasForeignKey(ur => ur.UserId)
            .IsRequired();

        builder.Entity<AppRole>()
            .HasMany(r => r.UserRoles)
            .WithOne(ur => ur.Role)
            .HasForeignKey(ur => ur.RoleId)
            .IsRequired();
    }

    /// <summary>
    /// Adds <c>WHERE NOT is_deleted</c> to every query over a soft-deletable
    /// entity, so no caller can forget it.
    /// </summary>
    private static void ApplySoftDeleteQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(property), parameter);

            entityType.SetQueryFilter(filter);
        }
    }

    /// <summary>
    /// Maps <see cref="BaseEntity.Version"/> onto PostgreSQL's system
    /// <c>xmin</c> column.
    /// </summary>
    /// <remarks>
    /// Optimistic concurrency for free: no extra column, no extra write. Two
    /// admins editing one product, or two checkouts drawing down the last unit
    /// of stock, produce a 409 rather than a silent overwrite.
    /// </remarks>
    private static void ApplyConcurrencyTokens(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var version = entityType.FindProperty(nameof(BaseEntity.Version));

            if (version is null)
            {
                continue;
            }

            version.SetColumnName("xmin");
            version.SetColumnType("xid");
            version.ValueGenerated = ValueGenerated.OnAddOrUpdate;
            version.IsConcurrencyToken = true;
        }
    }

    /// <summary>
    /// Forces every decimal to <c>numeric(18,2)</c> unless explicitly configured.
    /// </summary>
    /// <remarks>
    /// An un-configured decimal is provider-dependent, and a price column that
    /// rounds differently from the code that wrote it is a bug nobody finds
    /// until the accounts disagree.
    /// </remarks>
    private static void ApplyDecimalPrecision(ModelBuilder builder)
    {
        foreach (var property in builder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            if (property.GetColumnType() is null && property.GetPrecision() is null)
            {
                property.SetPrecision(18);
                property.SetScale(2);
            }
        }
    }
}
