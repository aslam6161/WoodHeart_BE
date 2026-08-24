namespace WoodHeart.Domain.Common;

/// <summary>
/// Stamped automatically by an EF Core interceptor. Entities never set these
/// themselves — if a handler assigns <c>CreatedAtUtc</c>, that is a bug.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAtUtc { get; set; }

    string? CreatedBy { get; set; }

    DateTimeOffset? ModifiedAtUtc { get; set; }

    string? ModifiedBy { get; set; }
}

/// <summary>
/// Marks an entity that is hidden rather than deleted.
/// </summary>
/// <remarks>
/// Applied through a global query filter, so soft-deleted rows disappear from
/// every query automatically. Products and orders are never hard-deleted: an
/// order line referencing a vanished product makes historic invoices
/// unprintable, and "delete" almost always means "stop showing this".
/// </remarks>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAtUtc { get; set; }

    string? DeletedBy { get; set; }
}
