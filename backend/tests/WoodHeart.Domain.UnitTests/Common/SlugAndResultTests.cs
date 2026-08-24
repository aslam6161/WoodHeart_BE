using WoodHeart.Domain.Common;

namespace WoodHeart.Domain.UnitTests.Common;

public class SlugTests
{
    [Theory]
    [InlineData("Segun Wood King Size Bed", "segun-wood-king-size-bed")]
    [InlineData("  Dining  Table  (6 Seater)  ", "dining-table-6-seater")]
    [InlineData("Décor Mirror", "decor-mirror")]
    [InlineData("Basin Cabinet — Matte White", "basin-cabinet-matte-white")]
    [InlineData("100% Solid Wood!", "100-solid-wood")]
    public void Product_names_become_clean_urls(string name, string expected)
    {
        Slug.From(name).Value.ShouldBe(expected);
    }

    [Fact]
    public void Bangla_names_produce_usable_slugs()
    {
        // Bangla characters are valid, indexable URL content — stripping them
        // would collapse every Bangla product name to an empty slug.
        var slug = Slug.From("সেগুন কাঠের খাট");

        slug.Value.ShouldNotBeEmpty();
        slug.Value.ShouldNotContain(" ");
    }

    [Fact]
    public void WithSuffix_resolves_a_name_collision()
    {
        Slug.From("dining-table").WithSuffix(2).Value.ShouldBe("dining-table-2");
    }

    [Fact]
    public void Slugs_are_truncated_to_the_column_length()
    {
        var slug = Slug.From(new string('a', 300));

        slug.Value.Length.ShouldBeLessThanOrEqualTo(Slug.MaxLength);
    }

    [Fact]
    public void Text_with_nothing_sluggable_throws()
    {
        Should.Throw<ArgumentException>(() => Slug.From("!!!"));
        Should.Throw<ArgumentException>(() => Slug.From("   "));
    }
}

public class ResultTests
{
    [Fact]
    public void A_successful_result_exposes_its_value()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        // Better to fail loudly at the call site than to return a default that
        // silently becomes a 0-taka order total three layers away.
        var result = Result.Failure<int>(Error.NotFound("catalog.product_not_found", "No such product."));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_failure_keeps_its_error_and_reports_no_success()
    {
        var error = Error.Conflict("promotions.coupon_already_used", "You have already used this coupon.");
        var result = Result.Failure(error);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Validation_errors_carry_per_field_messages()
    {
        // This dictionary is what the Angular reactive form binds to each control.
        var error = Error.Validation(
            "common.validation_failed",
            "One or more fields need your attention.",
            new Dictionary<string, string[]>
            {
                ["phoneNumber"] = ["Enter a valid Bangladeshi mobile number."],
                ["quantity"] = ["Quantity must be at least 1."]
            });

        error.ValidationErrors.ShouldNotBeNull();
        error.ValidationErrors!.Count.ShouldBe(2);
        error.ValidationErrors["phoneNumber"].ShouldHaveSingleItem();
    }

    [Fact]
    public void Match_collapses_both_branches()
    {
        var success = Result.Success(10);
        var failure = Result.Failure<int>(Error.Failure("x.y", "nope"));

        success.Match(v => $"ok:{v}", e => $"err:{e.Code}").ShouldBe("ok:10");
        failure.Match(v => $"ok:{v}", e => $"err:{e.Code}").ShouldBe("err:x.y");
    }

    [Fact]
    public void A_value_lifts_implicitly_into_a_success()
    {
        Result<string> result = "hello";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("hello");
    }

    [Fact]
    public void FirstFailureOr_short_circuits_on_the_first_problem()
    {
        var notFound = Error.NotFound("a.b", "missing");

        Result.FirstFailureOr(Result.Success(), Result.Success()).IsSuccess.ShouldBeTrue();
        Result.FirstFailureOr(Result.Success(), Result.Failure(notFound)).Error.ShouldBe(notFound);
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.External)]
    public void Error_type_survives_the_round_trip(ErrorType type)
    {
        var error = new Error("some.code", "Some description.", type);

        Result.Failure<int>(error).Error.Type.ShouldBe(type);
    }
}

public class LocalizedTextTests
{
    [Fact]
    public void Bangla_falls_back_to_english_when_untranslated()
    {
        // Lets the team launch in English and translate incrementally without
        // ever rendering a blank product name to a customer.
        var text = LocalizedText.Create("Dining Table");

        text.For("bn").ShouldBe("Dining Table");
        text.For("en").ShouldBe("Dining Table");
        text.HasBangla.ShouldBeFalse();
    }

    [Fact]
    public void Bangla_is_used_when_present()
    {
        var text = LocalizedText.Create("Dining Table", "খাবার টেবিল");

        text.For("bn").ShouldBe("খাবার টেবিল");
        text.For("bn-BD").ShouldBe("খাবার টেবিল");
        text.For("en").ShouldBe("Dining Table");
        text.For(null).ShouldBe("Dining Table");
    }

    [Fact]
    public void English_is_required()
    {
        Should.Throw<ArgumentException>(() => LocalizedText.Create(""));
    }

    [Fact]
    public void Whitespace_only_bangla_is_treated_as_absent()
    {
        LocalizedText.Create("Mirror", "   ").HasBangla.ShouldBeFalse();
    }
}
