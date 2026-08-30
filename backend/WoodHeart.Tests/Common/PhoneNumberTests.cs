using WoodHeart.Domain.Exceptions;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Tests.Common;

/// <summary>
/// The phone number is the customer's identity in this market, so normalisation
/// has to be right: the same person typing their number four different ways must
/// resolve to one stored value, or "have we seen this customer?" is unanswerable.
/// </summary>
public class PhoneNumberTests
{
    [Theory]
    [InlineData("01712345678")]        // how a customer types it
    [InlineData("+8801712345678")]     // E.164
    [InlineData("8801712345678")]      // country code, no plus
    [InlineData("1712345678")]         // leading zero dropped
    [InlineData("017-1234-5678")]      // dashes
    [InlineData("017 1234 5678")]      // spaces
    [InlineData(" 01712345678 ")]      // padding from a copy-paste
    public void All_common_input_formats_normalise_to_one_stored_value(string input)
    {
        PhoneNumber.TryParse(input, out var phone)
            .ShouldBeTrue($"'{input}' is a valid Bangladeshi mobile number");

        phone!.Value.ShouldBe("+8801712345678");
        phone.National.ShouldBe("01712345678");
    }

    [Fact]
    public void The_same_number_typed_differently_compares_equal()
    {
        // This is what makes guest-order claiming work: a guest who checked out
        // as "017-1234-5678" and later registers as "+8801712345678" is one person.
        var typed = PhoneNumber.Parse("017-1234-5678");
        var stored = PhoneNumber.Parse("+8801712345678");

        typed.ShouldBe(stored);
    }

    [Theory]
    [InlineData("013")]  // Grameenphone
    [InlineData("014")]  // Banglalink
    [InlineData("015")]  // Teletalk
    [InlineData("016")]  // Airtel
    [InlineData("017")]  // Grameenphone
    [InlineData("018")]  // Robi
    [InlineData("019")]  // Banglalink
    public void Every_live_operator_prefix_is_accepted(string prefix)
    {
        PhoneNumber.TryParse($"{prefix}12345678", out var phone)
            .ShouldBeTrue($"{prefix} is a live Bangladeshi operator prefix");

        phone!.OperatorPrefix.ShouldBe(prefix);
    }

    [Theory]
    [InlineData("01112345678")]     // 011 — retired prefix
    [InlineData("01212345678")]     // 012 — retired prefix
    [InlineData("0171234567")]      // one digit short
    [InlineData("017123456789")]    // one digit long
    [InlineData("+9171234567890")]  // Indian number
    [InlineData("abcdefghijk")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_numbers_are_rejected_with_a_helpful_error(string? input)
    {
        PhoneNumber.TryParse(input, out var phone).ShouldBeFalse();
        phone.ShouldBeNull();

        PhoneNumber.InvalidCode.ShouldBe("common.invalid_phone");

        // The message has to be something a customer can act on.
        PhoneNumber.InvalidMessage.ShouldContain("01712345678");
    }

    [Fact]
    public void Masked_hides_the_middle_digits_for_logs()
    {
        var phone = PhoneNumber.Parse("01712345678");

        phone.Masked.ShouldBe("017****5678");
        phone.Masked.ShouldNotContain("1234");
    }

    [Fact]
    public void Parse_throws_so_a_bad_number_can_never_be_mistaken_for_a_good_one()
    {
        // Parse is for places where a malformed number is a bug — seed data, or
        // a value already validated on the way in. Anything carrying user input
        // uses TryParse and returns a GeneralResponse failure instead.
        var exception = Should.Throw<DomainException>(() => PhoneNumber.Parse("nonsense"));

        exception.Code.ShouldBe(PhoneNumber.InvalidCode);
    }
}
