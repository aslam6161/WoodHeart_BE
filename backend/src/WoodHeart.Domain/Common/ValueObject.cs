namespace WoodHeart.Domain.Common;

/// <summary>
/// A value object has no identity — two instances with the same components
/// are the same thing. Money, PhoneNumber, Address, Slug, Dimensions.
/// </summary>
/// <remarks>
/// Value objects must be immutable. Any "change" produces a new instance.
/// This is what makes them safe to share and impossible to corrupt from a
/// distance, and it is why <c>Money</c> can validate its own invariants once
/// in a constructor instead of on every use.
/// </remarks>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>The components that define equality, in a stable order.</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null || other.GetType() != GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);
}
