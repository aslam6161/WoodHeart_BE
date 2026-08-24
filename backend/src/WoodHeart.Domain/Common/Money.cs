namespace WoodHeart.Domain.Common;

/// <summary>
/// An amount of money in a specific currency.
/// </summary>
/// <remarks>
/// <para>
/// Two rules this type exists to enforce:
/// </para>
/// <list type="number">
///   <item>
///     Money is <c>decimal</c>, never <c>double</c>. Binary floating point
///     cannot represent 0.1 exactly, so a few thousand additions of a VAT line
///     drift by whole taka and the accounts stop reconciling.
///   </item>
///   <item>
///     Currencies never mix silently. Adding BDT to USD throws instead of
///     producing a plausible-looking wrong number. v1 is BDT-only, but the
///     guard costs nothing now and is impossible to retrofit later.
///   </item>
/// </list>
/// <para>
/// Amounts are rounded to 2 decimal places with banker's rounding
/// (<see cref="MidpointRounding.ToEven"/>) at construction, so a stored value
/// always equals what the database column holds.
/// </para>
/// </remarks>
public sealed class Money : ValueObject, IComparable<Money>
{
    public const string Bdt = "BDT";
    public const int DecimalPlaces = 2;

    private Money(decimal amount, string currency)
    {
        Amount = decimal.Round(amount, DecimalPlaces, MidpointRounding.ToEven);
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public bool IsPositive => Amount > 0m;

    /// <summary>Zero taka — the identity element for cart and order totals.</summary>
    public static Money Zero(string currency = Bdt) => new(0m, currency);

    public static Money From(decimal amount, string currency = Bdt)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));
        }

        return new Money(amount, currency.ToUpperInvariant());
    }

    /// <summary>Taka convenience factory — the overwhelmingly common case.</summary>
    public static Money Taka(decimal amount) => From(amount, Bdt);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Divide(decimal divisor) => divisor == 0m
        ? throw new DivideByZeroException("Cannot divide money by zero.")
        : new Money(Amount / divisor, Currency);

    /// <summary>Applies a percentage, e.g. <c>price.Percentage(15m)</c> for 15% VAT.</summary>
    public Money Percentage(decimal percent) => new(Amount * percent / 100m, Currency);

    /// <summary>Clamps to zero. Discounts must never push a line total negative.</summary>
    public Money OrZeroIfNegative() => IsNegative ? Zero(Currency) : this;

    /// <summary>Caps the amount, used for a discount's MaxDiscountAmount.</summary>
    public Money CapAt(Money maximum)
    {
        EnsureSameCurrency(maximum);
        return Amount > maximum.Amount ? maximum : this;
    }

    /// <summary>
    /// Rounds to whole taka. Bangladeshi retail almost never quotes poisha, and
    /// COD in particular needs an amount the delivery rider can actually collect.
    /// </summary>
    public Money RoundToWholeTaka() =>
        new(decimal.Round(Amount, 0, MidpointRounding.AwayFromZero), Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money money, decimal factor) => money.Multiply(factor);

    // The relational operators express a business comparison — "is this order
    // over the free-delivery threshold?" — and comparing BDT to USD without an
    // exchange rate is a bug, so these guard.
    public static bool operator >(Money left, Money right) => left.CompareAmountTo(right) > 0;

    public static bool operator <(Money left, Money right) => left.CompareAmountTo(right) < 0;

    public static bool operator >=(Money left, Money right) => left.CompareAmountTo(right) >= 0;

    public static bool operator <=(Money left, Money right) => left.CompareAmountTo(right) <= 0;

    /// <summary>
    /// Total ordering over all money values: by currency first, then by amount.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately does NOT throw on mixed currencies, unlike the relational
    /// operators above. <see cref="IComparable{T}"/> is a general-purpose
    /// contract that sorting, <c>Comparer&lt;T&gt;.Default</c> and several test
    /// frameworks call on values they know nothing about — an implementation
    /// that throws turns an innocent <c>OrderBy</c> or equality assertion into a
    /// crash.
    /// </para>
    /// <para>
    /// Grouping by currency before amount keeps the ordering meaningful (all
    /// BDT together, ascending) without ever implying that 500 BDT and 500 USD
    /// are comparable in value.
    /// </para>
    /// </remarks>
    public int CompareTo(Money? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byCurrency = string.CompareOrdinal(Currency, other.Currency);

        return byCurrency != 0 ? byCurrency : Amount.CompareTo(other.Amount);
    }

    /// <summary>Compares amounts, requiring the same currency. Backs the relational operators.</summary>
    private int CompareAmountTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Currency} {Amount:N2}";

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot operate on {Currency} and {other.Currency} together.");
        }
    }
}
