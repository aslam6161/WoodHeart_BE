using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Consultations;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.Enums.Consultations;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Consultations;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Jobs;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Jobs;

/// <summary>
/// The reminder run: what it sends, what it stamps, and what it does not do
/// twice.
/// </summary>
/// <remarks>
/// The arithmetic is <c>BookingReminders</c>'s and tested there. What is
/// tested here is the part that touches the world: that the stamp and the
/// message are one write, that a second run sends nothing, and that a run
/// which throws on one booking still sends the rest.
/// </remarks>
public class BookingReminderJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 4, 0, 0, TimeSpan.Zero);

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly INotificationQueue _notifications = Substitute.For<INotificationQueue>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new(Now);

    private readonly List<NotificationRequest> _sent = [];
    private readonly BookingReminderJob _job;

    public BookingReminderJobTests()
    {
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None));

        _notifications
            .EnqueueAsync(Arg.Do<NotificationRequest>(_sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _job = new BookingReminderJob(
            _bookings, _notifications, _clock, _unitOfWork,
            NullLogger<BookingReminderJob>.Instance);
    }

    [Fact]
    public async Task Nothing_due_means_nothing_sent()
    {
        Due();

        (await _job.RunAsync()).ShouldBe(0);
        _sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_booking_tomorrow_is_reminded_about_and_stamped()
    {
        var booking = Booking(Now.AddHours(20));

        Due(booking);

        (await _job.RunAsync()).ShouldBe(1);

        var message = _sent.ShouldHaveSingleItem();

        message.Type.ShouldBe("booking.reminder");

        // Stamped in the same unit of work as the message. Stamping first
        // would lose a reminder to a crash in between; sending first would
        // send it twice.
        booking.FirstReminderSentAt.ShouldBe(Now);
        booking.FinalReminderSentAt.ShouldBeNull();

        _bookings.Received(1).Update(booking);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_booking_this_afternoon_gets_the_last_reminder()
    {
        var booking = Booking(Now.AddMinutes(90));

        Due(booking);

        await _job.RunAsync();

        booking.FinalReminderSentAt.ShouldBe(Now);
        booking.FirstReminderSentAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_second_run_sends_nothing()
    {
        var booking = Booking(Now.AddHours(20));

        Due(booking);

        await _job.RunAsync();
        _sent.Clear();

        // Same clock, same booking, now carrying its stamp.
        (await _job.RunAsync()).ShouldBe(0);
        _sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_message_says_the_time_in_Dhaka()
    {
        // 04:00 UTC on the 27th is ten in the morning in Dhaka, and ten in the
        // morning is what the customer has to be at the studio for.
        var booking = Booking(new DateTimeOffset(2026, 9, 27, 4, 0, 0, TimeSpan.Zero));

        Due(booking);

        await _job.RunAsync();

        using var payload = JsonDocument.Parse(_sent.ShouldHaveSingleItem().Payload);

        payload.RootElement.GetProperty("scheduledAt").GetString()
            .ShouldBe("Sunday 27 September, 10:00 AM");
    }

    [Fact]
    public async Task The_key_carries_the_time_so_a_moved_booking_is_reminded_about_again()
    {
        var booking = Booking(Now.AddHours(20));

        Due(booking);

        await _job.RunAsync();

        // A reschedule clears the stamps. Without the appointment's own time in
        // the key, the outbox would swallow the second reminder as a duplicate
        // of the first — and the customer would be left with the old time.
        var first = _sent.ShouldHaveSingleItem().IdempotencyKey;

        _sent.Clear();
        booking.FirstReminderSentAt = null;
        booking.ScheduledAtUtc = Now.AddHours(21);

        await _job.RunAsync();

        _sent.ShouldHaveSingleItem().IdempotencyKey.ShouldNotBe(first);
    }

    [Fact]
    public async Task One_booking_that_throws_does_not_cost_the_others_their_reminder()
    {
        var broken = Booking(Now.AddHours(20), number: "WHC-2609-00001");
        var fine = Booking(Now.AddHours(21), number: "WHC-2609-00002");

        Due(broken, fine);

        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("the gateway fell over"),
                call => call.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None));

        // The next run sees the broken one again; the log says why it is still
        // there. All-or-nothing across the batch would mean one bad row costs
        // every customer their reminder, every quarter of an hour.
        (await _job.RunAsync()).ShouldBe(1);

        fine.FirstReminderSentAt.ShouldBe(Now);
        broken.FirstReminderSentAt.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private void Due(params Booking[] bookings) =>
        _bookings.GetDueForReminderAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(bookings);

    private static Booking Booking(DateTimeOffset scheduledAt, string number = "WHC-2609-00042") =>
        new()
        {
            Id = 1,
            BookingNumber = number,
            ConsultationService = new ConsultationService
            {
                Name = LocalizedText.Create("Studio consultation"),
                Slug = Slug.From("studio-consultation"),
                Mode = ConsultationMode.InStudio,
                DurationMinutes = 60,
                Fee = Money.Taka(2_000m)
            },
            ContactName = "Rakib Hasan",
            ContactPhone = "+8801712349999",
            ScheduledAtUtc = scheduledAt,
            DurationMinutes = 60,
            Currency = Money.Bdt,
            Fee = Money.Taka(2_000m),
            Status = BookingStatus.Confirmed
        };
}
