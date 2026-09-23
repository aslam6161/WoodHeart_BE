using System.Text.Json;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Consultations;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Consultations;
using WoodHeart.Service.Interfaces.Jobs;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Service.Services.Jobs;

/// <inheritdoc cref="IBookingReminders" />
/// <remarks>
/// <para>
/// <b>The deciding is <see cref="BookingReminders"/>'s.</b> This fetches the
/// handful of bookings close enough to matter and stamps the ones it writes
/// for; which reminder each is owed, and whether it is owed one at all, is a
/// pure function tested without a database.
/// </para>
/// <para>
/// <b>One transaction per booking</b>, for the same reason the unpaid-order
/// job has one: a batch of forty reminders where the thirty-first throws
/// should still have sent thirty, and the next run should pick up the ten.
/// </para>
/// <para>
/// <b>The stamp and the message commit together.</b> Stamping first would lose
/// a reminder to a crash in between; sending first would send it twice. The
/// outbox row and the stamp are the same write, so "told them" and "recorded
/// that we told them" cannot come apart.
/// </para>
/// </remarks>
public class BookingReminderJob(
    IBookingRepository bookings,
    INotificationQueue notifications,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<BookingReminderJob> logger) : IBookingReminders
{
    /// <summary>
    /// Per run. Everything inside a day's horizon that still owes a message
    /// fits comfortably for a shop this size; the cap is there so that a
    /// database left alone for a week cannot turn one run into an hour.
    /// </summary>
    private const int BatchSize = 200;

    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var horizon = now.AddHours(BookingReminders.FirstHoursBefore);

        var due = await bookings.GetDueForReminderAsync(now, horizon, BatchSize, cancellationToken);
        var sent = 0;

        foreach (var booking in due)
        {
            // The query narrows; this decides. A booking inside the horizon
            // that has already had the reminder it is owed comes back null.
            if (BookingReminders.Due(
                    booking.ScheduledAtUtc,
                    now,
                    booking.FirstReminderSentAt,
                    booking.FinalReminderSentAt) is not { } window)
            {
                continue;
            }

            try
            {
                await unitOfWork.ExecuteInTransactionAsync(
                    ct => SendAsync(booking, window, now, ct), cancellationToken);

                sent++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The next run sees this booking again; the log says why it is
                // still there.
                ConsultationJobLog.ReminderFailed(logger, exception, booking.BookingNumber);
            }
        }

        if (sent > 0)
        {
            ConsultationJobLog.RemindersSent(logger, sent);
        }

        return sent;
    }

    private async Task<bool> SendAsync(
        Booking booking, int hoursBefore, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (hoursBefore == BookingReminders.FinalHoursBefore)
        {
            booking.FinalReminderSentAt = now;
        }
        else
        {
            booking.FirstReminderSentAt = now;
        }

        await notifications.EnqueueAsync(
            new NotificationRequest
            {
                Type = NotificationTemplates.BookingReminder,

                // The appointment's own time is part of the key. A booking that
                // is moved has its stamps cleared and is reminded about again —
                // and without the time in the key that second reminder, for the
                // new time, would be swallowed as a duplicate of the first.
                IdempotencyKey =
                    $"booking.reminder:{booking.BookingNumber}:{hoursBefore}:{booking.ScheduledAtUtc:O}",
                Payload = JsonSerializer.Serialize(new
                {
                    bookingNumber = booking.BookingNumber,
                    contactName = booking.ContactName,
                    contactPhone = booking.ContactPhone,
                    contactEmail = booking.ContactEmail,
                    language = booking.CustomerLanguage,
                    serviceName = booking.ConsultationService?.Name.En ?? string.Empty,
                    hoursBefore,

                    // The shop's own time, because the message is read by
                    // somebody in Dhaka and "14:00Z" means nothing to them.
                    scheduledAt = booking.ScheduledAtUtc
                        .ToOffset(SlotGenerator.DhakaOffset)
                        .ToString(
                            "dddd d MMMM, h:mm tt",
                            System.Globalization.CultureInfo.InvariantCulture)
                })
            },
            cancellationToken);

        bookings.Update(booking);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}

/// <summary>
/// Structured logging for the consultation jobs.
/// </summary>
/// <remarks>Event ids 2010–2011.</remarks>
internal static partial class ConsultationJobLog
{
    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Information,
        Message = "Queued {Count} consultation reminder(s).")]
    public static partial void RemindersSent(ILogger logger, int count);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Error,
        Message = "Could not queue the reminder for booking {BookingNumber}; it will be tried again next run.")]
    public static partial void ReminderFailed(ILogger logger, Exception exception, string bookingNumber);
}
