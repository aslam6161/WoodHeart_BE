using System.Text.RegularExpressions;

using WoodHeart.Domain.Exceptions;

namespace WoodHeart.Domain.ValueObjects;

/// <summary>
/// A Bangladeshi mobile number, normalised to E.164.
/// </summary>
/// <remarks>
/// <para>
/// The phone number is WoodHeart's primary customer handle, not the email
/// address: a large share of customers here shop without an email they check,
/// order confirmations arrive by SMS, and the delivery rider calls before
/// arriving. So it is a first-class value object with real validation rather
/// than a loose string.
/// </para>
/// <para>
/// Customers type the same number four different ways — <c>01712345678</c>,
/// <c>+8801712345678</c>, <c>8801712345678</c>, <c>017-1234-5678</c>. All four
/// normalise to <c>+8801712345678</c> for storage, so "have I seen this
/// customer before?" and "is this number already registered?" are answerable
/// with a simple equality check.
/// </para>
/// <para>
/// Valid operator prefixes: 013 (Grameenphone), 014 (Banglalink),
/// 015 (Teletalk), 016 (Airtel), 017 (Grameenphone), 018 (Robi),
/// 019 (Banglalink). 011/012 are retired and correctly rejected.
/// </para>
/// </remarks>
public sealed partial class PhoneNumber : ValueObject
{
    public const string CountryCode = "+880";

    private PhoneNumber(string e164, string national)
    {
        Value = e164;
        National = national;
    }

    /// <summary>Storage and comparison form: <c>+8801712345678</c>.</summary>
    public string Value { get; }

    /// <summary>Display form for Bangladeshi users: <c>01712345678</c>.</summary>
    public string National { get; }

    /// <summary>The operator prefix, e.g. <c>017</c>. Useful for SMS routing and analytics.</summary>
    public string OperatorPrefix => National[..3];

    /// <summary>Masked for logs and public display: <c>017****5678</c>.</summary>
    public string Masked => $"{National[..3]}****{National[^4..]}";

    public static bool TryParse(string? input, out PhoneNumber? phoneNumber)
    {
        phoneNumber = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        // Strip everything a human might type: spaces, dashes, dots, brackets.
        var digits = NonDigits().Replace(input, string.Empty);

        // 8801712345678 -> 01712345678 ; 1712345678 -> 01712345678
        var national = digits switch
        {
            { Length: 13 } when digits.StartsWith("880", StringComparison.Ordinal) => "0" + digits[3..],
            { Length: 11 } when digits.StartsWith('0') => digits,
            { Length: 10 } when digits.StartsWith('1') => "0" + digits,
            _ => null
        };

        if (national is null || !BangladeshMobile().IsMatch(national))
        {
            return false;
        }

        phoneNumber = new PhoneNumber(CountryCode + national[1..], national);
        return true;
    }

    /// <summary>The stable error code services return when parsing fails.</summary>
    public const string InvalidCode = "common.invalid_phone";

    /// <summary>The customer-facing message that accompanies <see cref="InvalidCode"/>.</summary>
    public const string InvalidMessage =
        "Enter a valid Bangladeshi mobile number, for example 01712345678.";

    /// <summary>
    /// Parses or throws. Use this only where a malformed number is genuinely a
    /// bug — seed data, a value already validated on the way in. Anything
    /// carrying user input should call <see cref="TryParse"/> and return a
    /// <c>GeneralResponse</c> failure instead, so the customer sees a 400 with
    /// a message rather than a 500.
    /// </summary>
    public static PhoneNumber Parse(string? input) =>
        TryParse(input, out var phone)
            ? phone!
            : throw new DomainException(InvalidCode, InvalidMessage);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex NonDigits();

    [GeneratedRegex(@"^01[3-9]\d{8}$")]
    private static partial Regex BangladeshMobile();
}
