using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace WoodHeart.Repository;

/// <summary>
/// The generic repository. Per-entity repositories derive from it and add only
/// the queries that are specific to them.
/// </summary>
/// <remarks>
/// Methods are <c>virtual</c> so a derived repository can override the default
/// — <c>ProductRepository.Query()</c>, for instance, will include media and
/// variants rather than making every caller remember the <c>Include</c>.
/// </remarks>
public class Repository<T>(DataContext context) : IRepository<T> where T : class
{
    protected DataContext Context { get; } = context;

    protected DbSet<T> Set { get; } = context.Set<T>();

    public virtual async Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
        await Set.FindAsync([id], cancellationToken);

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().ToListAsync(cancellationToken);

    public virtual async Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(predicate, cancellationToken);

    public virtual async Task<bool> AnyAsync(
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        await Set.AnyAsync(predicate, cancellationToken);

    public virtual async Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        predicate is null
            ? await Set.CountAsync(cancellationToken)
            : await Set.CountAsync(predicate, cancellationToken);

    public virtual async Task InsertAsync(T entity, CancellationToken cancellationToken = default) =>
        await Set.AddAsync(entity, cancellationToken);

    public virtual async Task InsertRangeAsync(
        IEnumerable<T> entities, CancellationToken cancellationToken = default) =>
        await Set.AddRangeAsync(entities, cancellationToken);

    public virtual void Update(T entity) => Set.Update(entity);

    public virtual void UpdateRange(IEnumerable<T> entities) => Set.UpdateRange(entities);

    public virtual void Delete(T entity) => Set.Remove(entity);

    public virtual void DeleteRange(IEnumerable<T> entities) => Set.RemoveRange(entities);

    public virtual void Detach(T entity) => Context.Entry(entity).State = EntityState.Detached;

    public virtual IQueryable<T> Query() => Set.AsQueryable();

    public virtual IQueryable<T> QueryNoTracking() => Set.AsNoTracking();
}
