using System.Text.RegularExpressions;

using WoodHeart.Domain.Exceptions;

namespace WoodHeart.Domain.ValueObjects;

/// <summary>
/// An email address, lower-cased for comparison.
/// </summary>
/// <remarks>
/// Optional throughout WoodHeart — <see cref="PhoneNumber"/> is the required
/// contact channel. Email is used for invoices, order documents and marketing,
/// all of which degrade gracefully when absent.
/// </remarks>
public sealed partial class EmailAddress : ValueObject
{
    public const int MaxLength = 254;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public string Domain => Value[(Value.IndexOf('@') + 1)..];

    /// <summary>Masked for logs: <c>as***@gmail.com</c>.</summary>
    public string Masked
    {
        get
        {
            var local = Value[..Value.IndexOf('@')];
            var visible = local.Length <= 2 ? local : local[..2];
            return $"{visible}***@{Domain}";
        }
    }

    public static bool TryParse(string? input, out EmailAddress? email)
    {
        email = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = input.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength || !Pattern().IsMatch(normalized))
        {
            return false;
        }

        email = new EmailAddress(normalized);
        return true;
    }

    /// <summary>The stable error code services return when parsing fails.</summary>
    public const string InvalidCode = "common.invalid_email";

    /// <summary>The customer-facing message that accompanies <see cref="InvalidCode"/>.</summary>
    public const string InvalidMessage = "Enter a valid email address.";

    /// <summary>Parses or throws. See the note on <see cref="PhoneNumber.Parse"/>.</summary>
    public static EmailAddress Parse(string? input) =>
        TryParse(input, out var email)
            ? email!
            : throw new DomainException(InvalidCode, InvalidMessage);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    // Deliberately permissive: the only real proof an address works is a
    // delivered message, and an over-strict pattern rejects valid customers.
    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$")]
    private static partial Regex Pattern();
}
