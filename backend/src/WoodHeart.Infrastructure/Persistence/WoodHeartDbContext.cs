using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WoodHeart.Application.Common.Abstractions;
using WoodHeart.Domain.Common;
using WoodHeart.Infrastructure.Identity;
using WoodHeart.Infrastructure.Persistence.Outbox;
using WoodHeart.Infrastructure.Persistence.Settings;

namespace WoodHeart.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the whole modular monolith.
/// </summary>
/// <remarks>
/// <para>
/// One context, not one per module. Separate contexts would forbid the very
/// thing a monolith exists to provide — a single transaction spanning an order
/// and its stock reservation — while buying only cosmetic separation. Module
/// boundaries are enforced by the repository ports and the architecture tests,
/// which is where enforcement actually belongs.
/// </para>
/// <para>
/// Entity configurations live in <c>Persistence/Configurations/{Module}/</c> and
/// are discovered by assembly scan, so this file never grows into a 2,000-line
/// <c>OnModelCreating</c>.
/// </para>
/// </remarks>
public sealed class WoodHeartDbContext(
    DbContextOptions<WoodHeartDbContext> options,
    IDateTimeProvider clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IUnitOfWork, IOutbox
{
    private readonly List<IDomainEvent> _pendingDomainEvents = [];
    private readonly List<OutboxMessage> _pendingOutbox = [];

    /// <summary>
    /// Set by <see cref="ExecuteInTransactionAsync{TResult}"/> so domain events are
    /// held until the outermost transaction actually commits.
    /// </summary>
    private bool _inExplicitTransaction;

    internal IDomainEventDispatcher? EventDispatcher { get; set; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<StoreSetting> StoreSettings => Set<StoreSetting>();

    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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
    // IOutbox
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stages an outbox message. It is written by the same <c>SaveChanges</c> as
    /// the business change, which is the whole point of the pattern.
    /// </summary>
    public void Enqueue(OutboxRequest request) =>
        _pendingOutbox.Add(new OutboxMessage
        {
            Type = request.Type,
            Payload = request.Payload,
            IdempotencyKey = request.IdempotencyKey,
            NotBeforeUtc = request.NotBeforeUtc,
            CreatedAtUtc = clock.UtcNow,
            CorrelationId = correlation.CorrelationId
        });

    // -------------------------------------------------------------------------
    // IUnitOfWork
    // -------------------------------------------------------------------------

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        ConvertDeletesToSoftDeletes();
        CollectDomainEvents();
        FlushPendingOutbox();

        var written = await base.SaveChangesAsync(cancellationToken);

        // Outside an explicit transaction the save IS the commit, so events can
        // go out now. Inside one, they wait for the real commit below.
        if (!_inExplicitTransaction)
        {
            await DispatchPendingDomainEventsAsync(cancellationToken);
        }

        return written;
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        SaveChangesAsync(cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        // Already inside a transaction (a handler dispatching another command):
        // join it rather than nesting, so the whole use case commits atomically.
        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using IDbContextTransaction transaction =
                await Database.BeginTransactionAsync(ct);

            _inExplicitTransaction = true;

            try
            {
                var result = await operation(ct);

                await transaction.CommitAsync(ct);
                _inExplicitTransaction = false;

                // Post-commit. A handler that fails here cannot undo a fact the
                // customer has already been told is true.
                await DispatchPendingDomainEventsAsync(ct);

                return result;
            }
            catch
            {
                _inExplicitTransaction = false;
                _pendingDomainEvents.Clear();
                _pendingOutbox.Clear();

                await transaction.RollbackAsync(ct);
                throw;
            }
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Save-time behaviour
    // -------------------------------------------------------------------------

    private void StampAuditFields()
    {
        var now = clock.UtcNow;
        var actor = currentUser.UserName ?? currentUser.UserId?.ToString() ?? "system";

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedBy = actor;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = now;
                    entry.Entity.ModifiedBy = actor;
                    break;
            }
        }
    }

    /// <summary>
    /// Turns a hard delete into a soft one for anything marked
    /// <see cref="ISoftDeletable"/>.
    /// </summary>
    /// <remarks>
    /// Intercepting here rather than trusting every caller to remember is what
    /// makes the guarantee real: an order line pointing at a vanished product
    /// would make historic invoices unprintable, and "delete this product"
    /// almost always means "stop offering it".
    /// </remarks>
    private void ConvertDeletesToSoftDeletes()
    {
        var now = clock.UtcNow;
        var actor = currentUser.UserName ?? currentUser.UserId?.ToString() ?? "system";

        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAtUtc = now;
            entry.Entity.DeletedBy = actor;
        }
    }

    private void CollectDomainEvents()
    {
        var roots = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var root in roots)
        {
            _pendingDomainEvents.AddRange(root.DomainEvents);
            root.ClearDomainEvents();
        }
    }

    private void FlushPendingOutbox()
    {
        if (_pendingOutbox.Count == 0)
        {
            return;
        }

        OutboxMessages.AddRange(_pendingOutbox);
        _pendingOutbox.Clear();
    }

    private async Task DispatchPendingDomainEventsAsync(CancellationToken cancellationToken)
    {
        if (_pendingDomainEvents.Count == 0 || EventDispatcher is null)
        {
            _pendingDomainEvents.Clear();
            return;
        }

        var events = _pendingDomainEvents.ToArray();
        _pendingDomainEvents.Clear();

        await EventDispatcher.DispatchAsync(events, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Model conventions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gives the ASP.NET Identity join tables the same snake_case names as the
    /// rest of the schema, so the database reads as one design rather than two.
    /// </summary>
    private static void RenameIdentityTables(ModelBuilder builder)
    {
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("user_tokens");
    }

    /// <summary>
    /// Adds <c>WHERE is_deleted = false</c> to every query over a soft-deletable
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

            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var property = System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var filter = System.Linq.Expressions.Expression.Lambda(
                System.Linq.Expressions.Expression.Not(property), parameter);

            entityType.SetQueryFilter(filter);
        }
    }

    /// <summary>
    /// Maps <see cref="AggregateRoot.Version"/> onto PostgreSQL's system
    /// <c>xmin</c> column.
    /// </summary>
    /// <remarks>
    /// Optimistic concurrency for free: no extra column, no extra write, and a
    /// second admin editing the same order gets a 409 instead of silently
    /// overwriting the first one's change.
    /// </remarks>
    private static void ApplyConcurrencyTokens(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(AggregateRoot).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var version = entityType.FindProperty(nameof(AggregateRoot.Version));

            if (version is null)
            {
                continue;
            }

            version.SetColumnName("xmin");
            version.SetColumnType("xid");
            version.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate;
            version.IsConcurrencyToken = true;
        }
    }

    /// <summary>
    /// Forces every decimal to <c>numeric(18,2)</c> unless explicitly configured.
    /// </summary>
    /// <remarks>
    /// Without this, an un-configured decimal silently becomes
    /// <c>numeric(18,2)</c> on some providers and full-precision on others, and
    /// a price column that rounds differently from the code that wrote it is a
    /// bug nobody finds until the accounts disagree.
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
