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
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
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
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
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
        sent.IdempotencyKey.ShouldBe($"booking.status:{BookingNumber}:Confirmed");
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
    // Fixtures
    // -------------------------------------------------------------------------

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
