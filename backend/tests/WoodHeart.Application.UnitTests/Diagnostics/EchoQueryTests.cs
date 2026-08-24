using WoodHeart.Application.Diagnostics;
using WoodHeart.Application.UnitTests.Common;
using WoodHeart.Domain.Common;

namespace WoodHeart.Application.UnitTests.Diagnostics;

/// <summary>
/// The reference example for how every handler in this codebase is tested:
/// construct it with fake ports, send the request, assert on the
/// <see cref="Result{TValue}"/>. No HTTP, no database, no mocking framework
/// unless a port genuinely needs one.
/// </summary>
public class EchoQueryTests
{
    private readonly FakeClock _clock = new();

    private EchoQueryHandler CreateHandler() => new(_clock);

    [Fact]
    public async Task Returns_the_message_with_both_timestamps()
    {
        var result = await CreateHandler().Handle(new EchoQuery("hello", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Message.ShouldBe("hello");
        result.Value.ReceivedAtUtc.ShouldBe(FakeClock.DefaultNow);
        result.Value.ReceivedAtDhaka.Offset.ShouldBe(TimeSpan.FromHours(6));
    }

    [Fact]
    public async Task Normalises_a_phone_number_and_masks_it_for_logging()
    {
        var result = await CreateHandler()
            .Handle(new EchoQuery("hi", "017-1234-5678"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NormalizedPhone.ShouldBe("+8801712345678");
        result.Value.MaskedPhone.ShouldBe("017****5678");
    }

    [Fact]
    public async Task Returns_a_failure_result_rather_than_throwing_on_a_bad_phone()
    {
        // The core convention: expected business failures are values, so the
        // pipeline can map them to a 400 without an exception ever unwinding.
        var result = await CreateHandler()
            .Handle(new EchoQuery("hi", "01112345678"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("common.invalid_phone");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task An_absent_phone_number_is_not_an_error()
    {
        // Phone is optional on this endpoint; email and phone are optional in
        // different places across the app, so "missing" must not mean "invalid".
        foreach (var input in new[] { null, "", "   " })
        {
            var result = await CreateHandler().Handle(new EchoQuery("hi", input), CancellationToken.None);

            result.IsSuccess.ShouldBeTrue($"'{input ?? "null"}' means the caller supplied no phone number");
            result.Value.NormalizedPhone.ShouldBeNull();
        }
    }

    [Fact]
    public async Task The_handler_reads_the_clock_it_was_given()
    {
        // Proves the handler has no hidden dependency on DateTime.UtcNow —
        // the property that makes every time-dependent feature testable later.
        var moment = new DateTimeOffset(2026, 12, 25, 3, 30, 0, TimeSpan.Zero);
        _clock.SetTo(moment);

        var result = await CreateHandler().Handle(new EchoQuery("hi", null), CancellationToken.None);

        result.Value.ReceivedAtUtc.ShouldBe(moment);
        result.Value.ReceivedAtDhaka.Hour.ShouldBe(9);   // 03:30 UTC = 09:30 Dhaka
    }
}

public class EchoQueryValidatorTests
{
    private readonly EchoQueryValidator _validator = new();

    [Fact]
    public void Accepts_a_normal_message()
    {
        _validator.Validate(new EchoQuery("hello", null)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_message(string message)
    {
        var result = _validator.Validate(new EchoQuery(message, null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(EchoQuery.Message));
    }

    [Fact]
    public void Rejects_a_message_over_the_column_length()
    {
        _validator.Validate(new EchoQuery(new string('a', 201), null)).IsValid.ShouldBeFalse();
    }
}
