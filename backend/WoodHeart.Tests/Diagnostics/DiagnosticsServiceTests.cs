using Microsoft.Extensions.Hosting;
using NSubstitute;
using WoodHeart.Service.DTOs.Common;
using WoodHeart.Service.Services.Common;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Diagnostics;

/// <summary>
/// The reference example for how every service in this codebase is tested:
/// construct it with fakes, call the method, assert on the
/// <c>GeneralResponse</c>. No HTTP, no database.
/// </summary>
public class DiagnosticsServiceTests
{
    private readonly FakeClock _clock = new();

    private DiagnosticsService CreateService()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");

        return new DiagnosticsService(_clock, environment);
    }

    [Fact]
    public void Echo_returns_the_message_with_both_timestamps()
    {
        var result = CreateService().Echo(new EchoRequestDto { Message = "hello" });

        result.IsSuccess.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Message.ShouldBe("hello");
        result.Data.ReceivedAtUtc.ShouldBe(FakeClock.DefaultNow);
        result.Data.ReceivedAtDhaka.Offset.ShouldBe(TimeSpan.FromHours(6));
    }

    [Fact]
    public void Echo_normalises_a_phone_number_and_masks_it_for_logging()
    {
        var result = CreateService()
            .Echo(new EchoRequestDto { Message = "hi", PhoneNumber = "017-1234-5678" });

        result.IsSuccess.ShouldBeTrue();
        result.Data!.NormalizedPhone.ShouldBe("+8801712345678");
        result.Data.MaskedPhone.ShouldBe("017****5678");
    }

    [Fact]
    public void Echo_returns_a_failure_rather_than_throwing_on_a_bad_phone()
    {
        // The core convention: expected business failures are values, so the
        // controller maps them to a 400 without an exception ever unwinding.
        var result = CreateService()
            .Echo(new EchoRequestDto { Message = "hi", PhoneNumber = "01112345678" });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe("common.invalid_phone");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_phone_number_is_not_an_error(string? input)
    {
        // Phone is optional here, as it is in several places across the app, so
        // "missing" must not be treated as "invalid".
        var result = CreateService().Echo(new EchoRequestDto { Message = "hi", PhoneNumber = input });

        result.IsSuccess.ShouldBeTrue();
        result.Data!.NormalizedPhone.ShouldBeNull();
    }

    [Fact]
    public void The_service_reads_the_clock_it_was_given()
    {
        // Proves there is no hidden dependency on DateTime.UtcNow — the property
        // that makes every time-dependent feature testable later.
        var moment = new DateTimeOffset(2026, 12, 25, 3, 30, 0, TimeSpan.Zero);
        _clock.SetTo(moment);

        var result = CreateService().Echo(new EchoRequestDto { Message = "hi" });

        result.Data!.ReceivedAtUtc.ShouldBe(moment);
        result.Data.ReceivedAtDhaka.Hour.ShouldBe(9);   // 03:30 UTC = 09:30 Dhaka
    }

    [Fact]
    public void Ping_reports_the_environment_and_both_clocks()
    {
        var result = CreateService().Ping();

        result.Status.ShouldBe("ok");
        result.Environment.ShouldBe("Testing");
        result.DhakaNow.Offset.ShouldBe(TimeSpan.FromHours(6));
    }
}
