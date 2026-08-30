namespace WoodHeart.Domain.Entity;

/// <summary>
/// Audit columns present on every persisted entity.
/// </summary>
/// <remarks>
/// These are stamped by <c>DataContext.SaveChangesAsync</c>, never by hand. If
/// a service assigns <see cref="CreatedAt"/> that is a bug — the context is the
/// single place that knows the request's clock and current user.
/// </remarks>
public interface IBaseEntity
{
    DateTimeOffset CreatedAt { get; set; }

    long? CreatedBy { get; set; }

    DateTimeOffset? UpdatedAt { get; set; }

    long? UpdatedBy { get; set; }
}

/// <summary>
/// Marks an entity that is hidden rather than removed.
/// </summary>
/// <remarks>
/// Applied through a global query filter, so soft-deleted rows disappear from
/// every query automatically. Products and orders are never hard-deleted: an
/// order line pointing at a vanished product makes historic invoices
/// unprintable, and "delete" almost always means "stop showing this".
/// </remarks>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAt { get; set; }

    long? DeletedBy { get; set; }
}
