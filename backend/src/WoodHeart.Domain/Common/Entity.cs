namespace WoodHeart.Domain.Common;

/// <summary>
/// Base class for every persisted domain object.
/// </summary>
/// <remarks>
/// Identity is a GUID v7 (time-ordered) rather than a sequential int:
/// it can be generated client-side before a round trip, it does not leak
/// order volume to competitors, and — unlike GUID v4 — it stays
/// index-friendly because it sorts by creation time.
/// <para>
/// Human-facing identifiers (OrderNumber, BookingNumber) are deliberately
/// separate values generated from a database sequence.
/// </para>
/// </remarks>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity() => Id = Guid.CreateVersion7();

    protected Entity(Guid id) => Id = id;

    public Guid Id { get; protected set; }

    /// <summary>True until the entity has been persisted for the first time.</summary>
    public bool IsTransient => Id == Guid.Empty;

    public bool Equals(Entity? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Different concrete types are never equal, even with the same Id.
        return GetType() == other.GetType() && !IsTransient && Id == other.Id;
    }

    public override bool Equals(object? obj) => obj is Entity entity && Equals(entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);

    public override string ToString() => $"{GetType().Name} [{Id}]";
}
