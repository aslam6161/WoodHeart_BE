using NSubstitute;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Service.Services.Common;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// The number a customer reads down the phone.
/// </summary>
/// <remarks>
/// The atomic part — that two simultaneous checkouts get different values — is
/// the database's job and is tested against a real one. What is tested here is
/// the part that has burned every shop that has ever built this: <b>the month
/// is Dhaka's month, not UTC's</b>.
/// </remarks>
public class NumberSequenceServiceTests
{
    private readonly INumberSequenceRepository _sequences = Substitute.For<INumberSequenceRepository>();

    private NumberSequenceService Service(FakeClock clock)
    {
        _sequences.ReserveNextAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(42);

        return new NumberSequenceService(_sequences, clock);
    }

    [Fact]
    public async Task An_order_number_reads_as_prefix_month_and_counter()
    {
        var number = await Service(new FakeClock()).NextAsync(NumberSequenceService.Orders, "WH");

        // Friday 2026-08-28 in the fake clock.
        number.ShouldBe("WH-2608-00042");
    }

    [Fact]
    public async Task The_counter_is_padded_so_the_numbers_line_up()
    {
        _sequences.ReserveNextAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(7);

        var number = await new NumberSequenceService(_sequences, new FakeClock())
            .NextAsync(NumberSequenceService.Orders, "WH");

        number.ShouldEndWith("-00007");
    }

    [Fact]
    public async Task Passing_five_digits_widens_the_number_rather_than_wrapping()
    {
        // 100,000 orders in one month is not this shop's problem — but a format
        // that silently truncated would issue a duplicate number, and the
        // unique index would then fail a real sale.
        _sequences.ReserveNextAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(123_456);

        var number = await new NumberSequenceService(_sequences, new FakeClock())
            .NextAsync(NumberSequenceService.Orders, "WH");

        number.ShouldBe("WH-2608-123456");
    }

    [Fact]
    public async Task The_month_is_Dhakas_month_and_not_UTCs()
    {
        // 2026-09-30 20:00 UTC is 2026-10-01 02:00 in Dhaka. An order placed
        // then belongs to October's numbering and October's sales — and a shop
        // reconciling a month end against UTC would be short by however many
        // orders arrived after 6pm on the last day.
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero));

        var number = await Service(clock).NextAsync(NumberSequenceService.Orders, "WH");

        number.ShouldStartWith("WH-2610-");
    }

    [Fact]
    public async Task The_period_it_reserves_against_is_the_period_it_prints()
    {
        // If these ever disagreed, two orders in the same displayed month would
        // be drawing from different counters and could collide.
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero));

        await Service(clock).NextAsync(NumberSequenceService.Orders, "WH");

        await _sequences.Received(1).ReserveNextAsync(
            NumberSequenceService.Orders, "2610", Arg.Any<CancellationToken>());
    }
}
