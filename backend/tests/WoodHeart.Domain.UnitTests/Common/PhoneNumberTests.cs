using WoodHeart.Domain.Common;

namespace WoodHeart.Domain.UnitTests.Common;

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
        var result = PhoneNumber.Create(input);

        result.IsSuccess.ShouldBeTrue($"'{input}' is a valid Bangladeshi mobile number");
        result.Value.Value.ShouldBe("+8801712345678");
        result.Value.National.ShouldBe("01712345678");
    }

    [Fact]
    public void The_same_number_typed_differently_compares_equal()
    {
        // This is what makes guest-order claiming work: a guest who checked out
        // as "017-1234-5678" and later registers as "+8801712345678" is one person.
        var typed = PhoneNumber.Create("017-1234-5678").Value;
        var stored = PhoneNumber.Create("+8801712345678").Value;

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
        var result = PhoneNumber.Create($"{prefix}12345678");

        result.IsSuccess.ShouldBeTrue($"{prefix} is a live Bangladeshi operator prefix");
        result.Value.OperatorPrefix.ShouldBe(prefix);
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
        var result = PhoneNumber.Create(input);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("common.invalid_phone");
        result.Error.Type.ShouldBe(ErrorType.Validation);

        // The message has to be something a customer can act on.
        result.Error.Description.ShouldContain("01712345678");
    }

    [Fact]
    public void Masked_hides_the_middle_digits_for_logs()
    {
        var phone = PhoneNumber.Create("01712345678").Value;

        phone.Masked.ShouldBe("017****5678");
        phone.Masked.ShouldNotContain("1234");
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_throws_rather_than_returning_null()
    {
        var result = PhoneNumber.Create("nonsense");

        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
