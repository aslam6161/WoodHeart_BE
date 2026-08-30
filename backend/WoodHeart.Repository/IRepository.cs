using System.Linq.Expressions;

namespace WoodHeart.Repository;

/// <summary>
/// Data access for one entity type. Stages changes; does not commit them.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is deliberately no <c>SaveAllAsync</c> here.</b> Bento's
/// <c>IRepository</c> has one, and its own <c>DOCs/bugs</c> folder records what
/// that costs: <c>notification-insert-commits-callers-pending-changes</c>. When
/// any repository can commit, a helper called halfway through a larger
/// operation flushes the caller's half-finished work along with its own, and
/// the resulting partial write is invisible until someone reconciles the data.
/// </para>
/// <para>
/// Committing lives on <see cref="IUnitOfWork"/> instead, so exactly one object
/// decides where a transaction ends — and it is always the service that owns
/// the use case, never a helper it happened to call.
/// </para>
/// </remarks>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);

    Task InsertAsync(T entity, CancellationToken cancellationToken = default);

    Task InsertRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    void Update(T entity);

    void UpdateRange(IEnumerable<T> entities);

    /// <summary>
    /// Removes the row — or, for an <c>ISoftDeletable</c> entity, flags it as
    /// deleted. The conversion happens in <c>DataContext.SaveChangesAsync</c>,
    /// so callers never have to remember which kind they are holding.
    /// </summary>
    void Delete(T entity);

    void DeleteRange(IEnumerable<T> entities);

    /// <summary>Stops tracking an entity, e.g. before re-attaching a detached copy.</summary>
    void Detach(T entity);

    /// <summary>Tracked query. Use when the results will be modified.</summary>
    IQueryable<T> Query();

    /// <summary>
    /// Untracked query — the right default for anything that only feeds a DTO.
    /// Product listing pages return dozens of rows per request and none of them
    /// are edited; tracking them is pure allocation.
    /// </summary>
    IQueryable<T> QueryNoTracking();
}
