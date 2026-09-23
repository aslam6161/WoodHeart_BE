using System.Text.RegularExpressions;
using WoodHeart.Domain.Consultations;
using WoodHeart.Domain.Enums.Consultations;

namespace WoodHeart.Tests.Consultations;

/// <summary>
/// Which booking moves are legal, and the one place that fact is written down
/// twice.
/// </summary>
public partial class BookingStatusMachineTests
{
    [Theory]
    [InlineData(BookingStatus.Requested, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Requested, BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.Completed)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.NoShow)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.Rescheduled)]
    [InlineData(BookingStatus.Rescheduled, BookingStatus.Completed)]
    public void The_ordinary_moves_are_allowed(BookingStatus from, BookingStatus to) =>
        BookingStatusMachine.CanTransition(from, to).ShouldBeTrue();

    [Theory]
    [InlineData(BookingStatus.Requested, BookingStatus.Completed)]
    [InlineData(BookingStatus.Requested, BookingStatus.NoShow)]
    [InlineData(BookingStatus.Cancelled, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.NoShow, BookingStatus.Completed)]
    public void The_ones_that_would_make_the_history_unreadable_are_not(
        BookingStatus from, BookingStatus to) =>
        BookingStatusMachine.CanTransition(from, to).ShouldBeFalse();

    [Fact]
    public void A_consultation_marked_done_by_mistake_can_be_put_back()
    {
        // Somebody presses Completed on the wrong row. The day's figures
        // should not have to stay wrong.
        BookingStatusMachine.CanTransition(BookingStatus.Completed, BookingStatus.Confirmed)
            .ShouldBeTrue();

        BookingStatusMachine.IsTerminal(BookingStatus.Completed).ShouldBeFalse();
    }

    [Theory]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.NoShow)]
    public void Both_endings_are_endings(BookingStatus status)
    {
        BookingStatusMachine.IsTerminal(status).ShouldBeTrue();
        BookingStatusMachine.NextFrom(status).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BookingStatus.Requested, true)]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Rescheduled, true)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void Only_a_live_booking_holds_the_slot(BookingStatus status, bool holds) =>
        BookingStatusMachine.HoldsTheSlot(status).ShouldBe(holds);

    /// <summary>
    /// The statuses in the unique index's filter are the statuses that hold a
    /// slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This fact is written down three times and this test is what keeps
    /// them together.</b> <c>BookingStatusMachine.HoldsTheSlot</c> decides what
    /// the calendar treats as taken; a list in <c>BookingRepository</c> decides
    /// what the query reads, because EF cannot translate a method call; and the
    /// filter on <c>ux_bookings_consultant_slot</c> decides what the database
    /// will actually refuse.
    /// </para>
    /// <para>
    /// If they drift, the failure is silent and expensive in both directions: a
    /// cancelled booking that still blocks an afternoon nobody can sell, or two
    /// customers in the studio at four o'clock.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_index_filter_names_exactly_the_statuses_that_hold_a_slot()
    {
        var configuration = File.ReadAllText(ConfigurationPath());

        var filter = IndexFilter().Match(configuration);

        filter.Success.ShouldBeTrue("the unique index on the consultant and the slot must have a filter");

        var named = Quoted()
            .Matches(filter.Groups[1].Value)
            .Select(match => Enum.Parse<BookingStatus>(match.Groups[1].Value))
            .ToHashSet();

        var holding = Enum.GetValues<BookingStatus>()
            .Where(BookingStatusMachine.HoldsTheSlot)
            .ToHashSet();

        named.ShouldBe(holding, ignoreOrder: true);
    }

    /// <summary>The configuration, found from the test assembly's own location.</summary>
    private static string ConfigurationPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "WoodHeart.Repository")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the repository project should be findable from the test output");

        return Path.Combine(
            directory.FullName,
            "WoodHeart.Repository",
            "Configurations",
            "Consultations",
            "ConsultationConfigurations.cs");
    }

    [GeneratedRegex(@"status IN \(([^)]*)\)", RegexOptions.Singleline)]
    private static partial Regex IndexFilter();

    [GeneratedRegex(@"'(\w+)'")]
    private static partial Regex Quoted();
}
