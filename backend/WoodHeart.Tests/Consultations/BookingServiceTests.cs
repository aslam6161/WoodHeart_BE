using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Consultations;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.Enums.Consultations;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Consultations;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Consultations;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Consultations;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Consultations;

/// <summary>
/// Booking an appointment: what is snapshotted, what is refused, and who is
/// told.
/// </summary>
public class BookingServiceTests
{
    private const string BookingNumber = "WHC-2609-00001";
    private const long ServiceId = 3;
    private const long RakibId = 7;
    private const long NadiaId = 8;

    private static readonly DateOnly Sunday = new(2026, 9, 27);
    private static readonly DateTimeOffset Slot = SlotGenerator.ToUtc(Sunday, new TimeOnly(10, 0));

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IConsultationServiceRepository _services =
        Substitute.For<IConsultationServiceRepository>();
    private readonly IAvailabilityService _availability = Substitute.For<IAvailabilityService>();
    private readonly INumberSequenceService _numbers = Substitute.For<INumberSequenceService>();
    private readonly INotificationQueue _notifications = Substitute.For<INotificationQueue>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 20, 6, 0, 0, TimeSpan.Zero));

    private readonly List<Booking> _inserted = [];
    private readonly BookingService _service;

    public BookingServiceTests()
    {
        _currentUser.Language.Returns("en");

        _numbers.NextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BookingNumber);

        _services.GetByIdAsync(ServiceId, Arg.Any<CancellationToken>()).Returns(StudioVisit());

        _availability
            .ResolveAsync(
                Arg.Any<ConsultationService>(), Arg.Any<long?>(),
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns([new ConsultantSlots(RakibId, "Rakib", [Slot])]);

        _bookings.InsertAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => _inserted.Add(call.Arg<Booking>()));

        // The real unit of work runs the delegate inside a transaction; the
        // substitute just runs it, so these tests exercise the body.
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<BookingDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse<BookingDto>>>>()(CancellationToken.None));

        _service = new BookingService(
            _bookings, _services, _availability, _numbers, _notifications,
            _currentUser, _clock, _unitOfWork, NullLogger<BookingService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Booking
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_guest_can_book_and_everything_about_it_is_frozen()
    {
        var result = await _service.CreateAsync(Request(), idempotencyKey: null);

        result.IsSuccess.ShouldBeTrue(result.Message);

        var booking = _inserted.ShouldHaveSingleItem();

        // No account, and that is the main path here rather than an edge case.
        booking.CustomerId.ShouldBeNull();
        booking.ContactPhone.ShouldBe("+8801712349999");
        booking.Status.ShouldBe(BookingStatus.Requested);

        // Snapshotted from the service: raising the price next month must not
        // rewrite what this customer was quoted.
        booking.DurationMinutes.ShouldBe(60);
        booking.Fee.ShouldBe(Money.Taka(2_000m));
        booking.AdvanceDue.ShouldBe(Money.Taka(500m));

        booking.Timeline.ShouldHaveSingleItem().ToStatus.ShouldBe(BookingStatus.Requested);
    }

    [Fact]
    public async Task Asking_for_anybody_assigns_somebody()
    {
        // An unassigned booking is nobody's afternoon, and the unique index
        // that stops a double booking is on the consultant — so leaving it null
        // would leave the slot unprotected.
        var result = await _service.CreateAsync(Request(consultantId: null), idempotencyKey: null);

        result.IsSuccess.ShouldBeTrue(result.Message);
        _inserted.ShouldHaveSingleItem().ConsultantId.ShouldBe(RakibId);
    }

    [Fact]
    public async Task The_first_consultant_free_at_that_time_takes_it()
    {
        _availability
            .ResolveAsync(
                Arg.Any<ConsultationService>(), Arg.Any<long?>(),
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ConsultantSlots(RakibId, "Rakib", [Slot.AddHours(2)]),
                new ConsultantSlots(NadiaId, "Nadia", [Slot])
            ]);

        await _service.CreateAsync(Request(consultantId: null), idempotencyKey: null);

        // Rakib is first in the list but is not free at ten; the slot decides,
        // not the order of the consultants.
        _inserted.ShouldHaveSingleItem().ConsultantId.ShouldBe(NadiaId);
    }

    [Fact]
    public async Task A_time_the_schedule_never_offered_is_refused()
    {
        // The client sends a time, not an availability decision. A request
        // naming midnight is refused however it was constructed.
        var result = await _service.CreateAsync(
            Request(startUtc: Slot.AddHours(-6)), idempotencyKey: null);

        result.ErrorCode.ShouldBe(ConsultationErrors.SlotNotAvailable);
        _inserted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_site_visit_needs_somewhere_to_visit()
    {
        _services.GetByIdAsync(ServiceId, Arg.Any<CancellationToken>()).Returns(SiteVisit());

        var result = await _service.CreateAsync(Request(), idempotencyKey: null);

        result.ErrorCode.ShouldBe(ConsultationErrors.SiteAddressRequired);
        _inserted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_site_visit_with_an_address_records_it()
    {
        _services.GetByIdAsync(ServiceId, Arg.Any<CancellationToken>()).Returns(SiteVisit());

        var result = await _service.CreateAsync(
            Request(site: new DeliveryAddressDto
            {
                Division = "Dhaka",
                District = "Dhaka",
                Area = "Banani",
                AddressLine = "Road 11"
            }),
            idempotencyKey: null);

        result.IsSuccess.ShouldBeTrue(result.Message);
        _inserted.ShouldHaveSingleItem().SiteAddress.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_number_that_is_not_a_mobile_is_refused_before_anything_is_read()
    {
        var result = await _service.CreateAsync(
            Request(phone: "12345"), idempotencyKey: null);

        result.ErrorCode.ShouldBe(ConsultationErrors.ContactPhoneInvalid);
        await _services.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default);
    }

    [Fact]
    public async Task A_second_press_of_Book_finds_the_first_booking()
    {
        _bookings.GetByIdempotencyKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Requested));

        var result = await _service.CreateAsync(Request(), idempotencyKey: "key-1");

        result.IsSuccess.ShouldBeTrue();
        result.Data!.BookingNumber.ShouldBe(BookingNumber);

        // Nothing written: a double-tap on a slow connection must not block a
        // second afternoon.
        _inserted.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_customer_is_told_it_was_requested_rather_than_confirmed()
    {
        NotificationRequest? sent = null;

        await _notifications.EnqueueAsync(
            Arg.Do<NotificationRequest>(request => sent = request), Arg.Any<CancellationToken>());

        await _service.CreateAsync(Request(), idempotencyKey: null);

        sent.ShouldNotBeNull();
        sent.Type.ShouldBe("booking.requested");

        // Keyed on the booking, so a retry of the worker cannot bill the shop
        // twice at the SMS gateway.
        sent.IdempotencyKey.ShouldBe($"booking.requested:{BookingNumber}");
    }

    // -------------------------------------------------------------------------
    // Afterwards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Staff_confirming_a_booking_tells_the_customer()
    {
        var booking = Existing(BookingStatus.Requested);

        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>()).Returns(booking);
        _currentUser.PhoneNumber.Returns("+8801712345678");

        NotificationRequest? sent = null;

        await _notifications.EnqueueAsync(
            Arg.Do<NotificationRequest>(request => sent = request), Arg.Any<CancellationToken>());

        var result = await _service.SetStatusAsync(BookingNumber, BookingStatus.Confirmed, "See you then.");

        result.IsSuccess.ShouldBeTrue(result.Message);
        booking.Status.ShouldBe(BookingStatus.Confirmed);

        var entry = booking.Timeline.ShouldHaveSingleItem();
        entry.FromStatus.ShouldBe(BookingStatus.Requested);
        entry.ToStatus.ShouldBe(BookingStatus.Confirmed);
        entry.ActorName.ShouldBe("+8801712345678");

        sent.ShouldNotBeNull();

        // Keyed on the moment of the move rather than on the status alone: a
        // booking confirmed, moved and confirmed again would otherwise have its
        // second confirmation swallowed as a duplicate, and the customer would
        // be left holding a time that had changed since.
        sent.IdempotencyKey.ShouldBe(
            $"booking.status:{BookingNumber}:Confirmed:{_clock.UtcNow:O}");
    }

    [Fact]
    public async Task The_board_is_told_which_moves_are_legal_from_here()
    {
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Requested));

        var result = await _service.GetForStaffAsync(BookingNumber);

        // Sent rather than reimplemented in Angular. A second copy of the graph
        // on the client drifts, and the drift is a button that renders, is
        // pressed, and comes back 409.
        result.Data!.AllowedStatusTransitions.ShouldBe(
            BookingStatusMachine.NextFrom(BookingStatus.Requested), ignoreOrder: true);

        result.Data.AllowedStatusTransitions.ShouldNotContain(BookingStatus.Completed);
    }

    [Fact]
    public async Task A_booking_that_is_over_offers_no_moves_at_all()
    {
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Cancelled));

        (await _service.GetForStaffAsync(BookingNumber)).Data!
            .AllowedStatusTransitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Staff_moving_a_booking_are_answered_with_the_number_they_must_ring()
    {
        var booking = Existing(BookingStatus.Requested);

        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>()).Returns(booking);

        var staff = await _service.SetStatusAsync(BookingNumber, BookingStatus.Confirmed, null);

        // The board confirms a booking and then draws the row it was handed.
        // Answering that write with the customer's masked number would put a
        // number nobody can ring on the one screen that exists to ring it.
        staff.Data!.ContactPhone.ShouldBe("+8801712349999");
    }

    [Fact]
    public async Task A_customer_cancelling_their_own_still_sees_it_masked()
    {
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Confirmed));

        var mine = await _service.CancelAsync(BookingNumber, "01712349999", null);

        mine.Data!.ContactPhone.ShouldContain("*");
    }

    [Fact]
    public async Task A_move_the_machine_forbids_is_refused()
    {
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Requested));

        var result = await _service.SetStatusAsync(BookingNumber, BookingStatus.Completed, null);

        result.ErrorCode.ShouldBe(ConsultationErrors.TransitionInvalid);
    }

    [Fact]
    public async Task A_guest_cancels_by_quoting_the_number_they_booked_with()
    {
        var booking = Existing(BookingStatus.Confirmed);

        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>()).Returns(booking);

        var result = await _service.CancelAsync(BookingNumber, "01712349999", "Something came up.");

        result.IsSuccess.ShouldBeTrue(result.Message);
        booking.Status.ShouldBe(BookingStatus.Cancelled);
    }

    [Fact]
    public async Task A_guessed_booking_number_on_its_own_discloses_nothing()
    {
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Confirmed));

        var result = await _service.GetAsync(BookingNumber, contactPhone: null);

        // Not-found rather than forbidden, so the endpoint cannot be used to
        // discover which booking numbers exist.
        result.ErrorCode.ShouldBe(ConsultationErrors.BookingNotFound);
    }

    [Fact]
    public async Task A_consultation_that_has_happened_can_no_longer_be_cancelled_by_the_customer()
    {
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Completed));

        var result = await _service.CancelAsync(BookingNumber, "01712349999", null);

        result.ErrorCode.ShouldBe(ConsultationErrors.NotCancellable);
    }

    [Fact]
    public async Task Staff_see_the_number_they_have_to_telephone()
    {
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Requested));

        var mine = await _service.GetAsync(BookingNumber, "01712349999");
        var theirs = await _service.GetForStaffAsync(BookingNumber);

        // Masked for the customer, whose confirmation page gets screenshotted.
        mine.Data!.ContactPhone.ShouldContain("*");
        theirs.Data!.ContactPhone.ShouldBe("+8801712349999");
    }

    // -------------------------------------------------------------------------
    // Moving one
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Moving_a_booking_records_where_it_came_from()
    {
        var booking = Existing(BookingStatus.Confirmed);
        var moved = Slot.AddHours(2);

        Free(moved);
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>()).Returns(booking);

        var result = await _service.RescheduleAsync(
            BookingNumber, moved, consultantId: null, "The consultant is at a site visit.");

        result.IsSuccess.ShouldBeTrue(result.Message);

        booking.ScheduledAtUtc.ShouldBe(moved);
        booking.Status.ShouldBe(BookingStatus.Rescheduled);

        // So that "moved from ten o'clock" reads correctly a month later,
        // rather than the customer's copy of the booking and the shop's
        // disagreeing about what was ever agreed.
        booking.PreviousScheduledAtUtc.ShouldBe(Slot);

        booking.Timeline.ShouldHaveSingleItem().ToStatus.ShouldBe(BookingStatus.Rescheduled);
    }

    [Fact]
    public async Task The_booking_being_moved_is_taken_out_of_the_diary_first()
    {
        var booking = Existing(BookingStatus.Confirmed);

        Free(Slot.AddMinutes(30));
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>()).Returns(booking);

        await _service.RescheduleAsync(
            BookingNumber, Slot.AddMinutes(30), consultantId: null, null);

        // Without the exclusion a booking could not be shifted by half an hour:
        // it would collide with the afternoon it is itself occupying.
        await _availability.Received().ResolveAsync(
            Arg.Any<ConsultationService>(),
            Arg.Any<long?>(),
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            booking.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moving_a_booking_undoes_the_reminders_it_has_already_had()
    {
        var booking = Existing(BookingStatus.Confirmed);

        booking.FirstReminderSentAt = _clock.UtcNow.AddHours(-1);

        Free(Slot.AddHours(2));
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>()).Returns(booking);

        await _service.RescheduleAsync(BookingNumber, Slot.AddHours(2), null, null);

        // The reminder the customer has already had was about a time this
        // appointment is no longer at. Clearing the stamp is what makes them
        // hear about the new one.
        booking.FirstReminderSentAt.ShouldBeNull();
        booking.FinalReminderSentAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_move_to_the_time_it_is_already_at_is_refused()
    {
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Confirmed));

        var result = await _service.RescheduleAsync(BookingNumber, Slot, RakibId, null);

        // Not a quiet no-op: it would write a timeline entry and send "we have
        // moved you" about a move that did not happen.
        result.ErrorCode.ShouldBe(ConsultationErrors.SlotUnchanged);
    }

    [Fact]
    public async Task A_move_to_a_time_nobody_is_free_at_is_refused()
    {
        var booking = Existing(BookingStatus.Confirmed);

        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>()).Returns(booking);

        var result = await _service.RescheduleAsync(
            BookingNumber, Slot.AddHours(9), consultantId: null, null);

        // The board is checked against the same schedule the booking page is,
        // so staff cannot put a customer somewhere a customer could not have.
        result.ErrorCode.ShouldBe(ConsultationErrors.SlotNotAvailable);
        booking.ScheduledAtUtc.ShouldBe(Slot);
    }

    [Fact]
    public async Task A_cancelled_booking_cannot_be_moved()
    {
        _bookings.GetByNumberAsync(BookingNumber, Arg.Any<CancellationToken>())
            .Returns(Existing(BookingStatus.Cancelled));

        var result = await _service.RescheduleAsync(BookingNumber, Slot.AddHours(2), null, null);

        result.ErrorCode.ShouldBe(ConsultationErrors.TransitionInvalid);
    }

    // -------------------------------------------------------------------------
    // The board
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_unfiltered_board_opens_on_today()
    {
        BookingSearch criteria = default;

        _bookings.SearchAsync(
                Arg.Do<BookingSearch>(value => criteria = value), Arg.Any<CancellationToken>())
            .Returns(new PagedList<Booking>([], 0, 1, 20));

        await _service.SearchAsync(new BookingQueryDto());

        // A diary opened at the oldest booking the shop ever took is no use to
        // anybody; the page staff want is this week.
        criteria.FromUtc.ShouldBe(SlotGenerator.ToUtc(_clock.DhakaToday, TimeOnly.MinValue));
        criteria.ToUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Searching_for_one_booking_looks_past_today()
    {
        BookingSearch criteria = default;

        _bookings.SearchAsync(
                Arg.Do<BookingSearch>(value => criteria = value), Arg.Any<CancellationToken>())
            .Returns(new PagedList<Booking>([], 0, 1, 20));

        await _service.SearchAsync(new BookingQueryDto { Term = "01712349999" });

        // Somebody hunting one booking is not browsing the diary, and the
        // booking they want may well be last month's.
        criteria.FromUtc.ShouldBeNull();
        criteria.Term.ShouldBe("01712349999");
    }

    [Fact]
    public async Task A_day_asked_for_is_the_shops_day_and_not_the_servers()
    {
        BookingSearch criteria = default;

        _bookings.SearchAsync(
                Arg.Do<BookingSearch>(value => criteria = value), Arg.Any<CancellationToken>())
            .Returns(new PagedList<Booking>([], 0, 1, 20));

        await _service.SearchAsync(new BookingQueryDto { From = Sunday, To = Sunday });

        // "Bookings on the 27th" means the 27th as Dhaka lives it: from six in
        // the evening UTC on the 26th to six on the 27th. Read as UTC dates it
        // would show six hours of the wrong day at each end.
        criteria.FromUtc.ShouldBe(SlotGenerator.ToUtc(Sunday, TimeOnly.MinValue));
        criteria.ToUtc.ShouldBe(SlotGenerator.ToUtc(Sunday.AddDays(1), TimeOnly.MinValue));
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    /// <summary>Makes exactly these times free, and nothing else.</summary>
    private void Free(params DateTimeOffset[] slots) =>
        _availability
            .ResolveAsync(
                Arg.Any<ConsultationService>(), Arg.Any<long?>(),
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns([new ConsultantSlots(RakibId, "Rakib", slots)]);


    private static CreateBookingDto Request(
        long? consultantId = RakibId,
        DateTimeOffset? startUtc = null,
        string phone = "01712349999",
        DeliveryAddressDto? site = null) =>
        new()
        {
            ServiceId = ServiceId,
            ConsultantId = consultantId,
            StartUtc = startUtc ?? Slot,
            ContactName = "Rakib Hasan",
            ContactPhone = phone,
            SiteAddress = site,
            ProjectBrief = "A bedroom for two children.",
            RoomTypes = ["Bedroom"]
        };

    private static ConsultationService StudioVisit() =>
        new()
        {
            Id = ServiceId,
            Name = LocalizedText.Create("Studio consultation"),
            Slug = Slug.From("studio-consultation"),
            Mode = ConsultationMode.InStudio,
            DurationMinutes = 60,
            Fee = Money.Taka(2_000m),
            RequiresAdvance = true,
            AdvanceAmount = Money.Taka(500m),
            IsActive = true
        };

    private static ConsultationService SiteVisit()
    {
        var service = StudioVisit();

        service.Mode = ConsultationMode.SiteVisit;

        return service;
    }

    private static Booking Existing(BookingStatus status) =>
        new()
        {
            Id = 1,
            BookingNumber = BookingNumber,
            ConsultationServiceId = ServiceId,
            ConsultationService = StudioVisit(),
            ConsultantId = RakibId,
            ContactName = "Rakib Hasan",
            ContactPhone = "+8801712349999",
            ScheduledAtUtc = Slot,
            DurationMinutes = 60,
            Currency = Money.Bdt,
            Fee = Money.Taka(2_000m),
            Status = status
        };
}
