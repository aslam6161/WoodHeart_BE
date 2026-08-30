namespace WoodHeart.Domain.Exceptions;

/// <summary>
/// Thrown when an aggregate is asked to do something that would break an
/// invariant — shipping a cancelled order, reserving negative stock.
/// </summary>
/// <remarks>
/// This is deliberately rare. Expected business failures ("coupon expired",
/// "out of stock") are returned as <c>GeneralResponse</c> failures from the
/// service layer; reaching this exception means a caller bypassed a check it
/// should have made, i.e. a bug.
/// The API maps it to 500 and logs it loudly, precisely because it should
/// never fire in production.
/// </remarks>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Guard clauses for aggregate invariants.</summary>
public static class DomainGuard
{
    public static void Against(bool condition, string code, string message)
    {
        if (condition)
        {
            throw new DomainException(code, message);
        }
    }

    public static T NotNull<T>(T? value, string code, string message) where T : class =>
        value ?? throw new DomainException(code, message);

    public static void NotNegative(decimal value, string code, string message) =>
        Against(value < 0, code, message);

    public static void Positive(int value, string code, string message) =>
        Against(value <= 0, code, message);

    public static void NotNullOrWhiteSpace(string? value, string code, string message) =>
        Against(string.IsNullOrWhiteSpace(value), code, message);
}
