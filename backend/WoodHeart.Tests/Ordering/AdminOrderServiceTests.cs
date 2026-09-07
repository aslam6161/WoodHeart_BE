using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Ordering;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// Orders from behind the counter.
/// </summary>
/// <remarks>
/// <para>
/// The four behaviours that carry money or accountability: <b>an illegal status
/// move is refused rather than applied</b>, <b>delivering a cash-on-delivery
/// order marks it paid</b>, <b>the delivery charge can be corrected before
/// anything is collected and not afterwards</b>, and <b>every change writes a
/// timeline entry naming who made it</b>.
/// </para>
/// <para>
/// The delivery override is the one worth the most attention. It is the whole
/// point of pricing delivery per product — a bed and its two bedside tables
/// price as three deliveries and go out on one van — and it is also the only
/// place in the shop where one member of staff can change what a customer is
/// charged. So it is tested for the arithmetic staying consistent, for the
/// lock, and for the audit trail.
/// </para>
/// </remarks>
public class AdminOrderServiceTests
{
    private const string OrderNumber = "WH-2609-00042";
    private const long StaffId = 7;

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly UserManager<AppUser> _users = MockUserManager();
    private readonly INotificationQueue _notifications = Substitute.For<INotificationQueue>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();

    public AdminOrderServiceTests()
    {
        _currentUser.UserId.Returns(StaffId);

        _users.FindByIdAsync("7").Returns(new AppUser
        {
            Id = StaffId,
            UserName = "01712345678",
            FullName = "Rakib Hasan"
        });
    }

    private AdminOrderService CreateService() =>
        new(_orders,
            _users,
            _notifications,
            _currentUser,
            _clock,
            _unitOfWork,
            NullLogger<AdminOrderService>.Instance);

    // -------------------------------------------------------------------------
    // The work axis
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Staff_can_move_an_order_forward()
    {
        var order = GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Processing });

        result.IsSuccess.ShouldBeTrue(result.Message);
        order.Status.ShouldBe(OrderStatus.Processing);
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_move_the_machine_forbids_is_refused()
    {
        // Confirmed straight to Shipped skips the building of the thing. The
        // machine is the single table this reads from, so the API and the
        // admin screen cannot disagree about it.
        var order = GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Shipped });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.OrderTransitionInvalid);
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task Setting_the_status_it_already_has_is_not_an_error()
    {
        // Two operators with the same order open. The second press should do
        // nothing, not raise a red banner about a conflict that is not one.
        var order = GivenAnOrder(OrderStatus.Processing);

        var result = await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Processing });

        result.IsSuccess.ShouldBeTrue(result.Message);
        order.Timeline.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancellation_must_say_why()
    {
        // This is the entry somebody is asked about six months later, and a
        // blank is not an answer.
        var order = GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Cancelled });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.ReasonRequired);
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task A_cancellation_with_a_reason_goes_through_and_keeps_it()
    {
        var order = GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().ChangeStatusAsync(
            OrderNumber,
            new ChangeOrderStatusDto
            {
                Status = OrderStatus.Cancelled,
                Note = "Customer found the same bed cheaper."
            });

        result.IsSuccess.ShouldBeTrue(result.Message);
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.Timeline.Single().Note.ShouldBe("Customer found the same bed cheaper.");
    }

    [Fact]
    public async Task Every_change_records_who_made_it()
    {
        // Copied onto the row rather than joined, so a member of staff who
        // leaves and is deactivated does not erase their name from the record
        // of what they did.
        var order = GivenAnOrder(OrderStatus.Confirmed);

        await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Processing });

        var entry = order.Timeline.Single();
        entry.ActorName.ShouldBe("Rakib Hasan");
        entry.ActorUserId.ShouldBe(StaffId);
        entry.FromStatus.ShouldBe(OrderStatus.Confirmed);
        entry.ToStatus.ShouldBe(OrderStatus.Processing);
        entry.OccurredAt.ShouldBe(_clock.UtcNow);
    }

    // -------------------------------------------------------------------------
    // What the work axis settles on the other two
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delivering_a_cash_on_delivery_order_marks_it_paid()
    {
        // On this method delivery *is* the collection — a refused doorstep
        // comes back as Returned, never as Delivered. Leaving it Unpaid would
        // make every completed COD order in the shop read as money outstanding.
        var order = GivenAnOrder(OrderStatus.Shipped);
        order.PaymentMethodCode = PaymentMethodCodes.CashOnDelivery;
        order.PaymentStatus = PaymentStatus.Unpaid;

        await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Delivered });

        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        order.FulfilmentStatus.ShouldBe(FulfilmentStatus.Fulfilled);
    }

    [Fact]
    public async Task Delivering_a_gateway_order_does_not_touch_the_money()
    {
        // Its money arrived at its own moment, and this is not it. An order
        // that failed at the gateway must not become paid because a van
        // turned up.
        var order = GivenAnOrder(OrderStatus.Shipped);
        order.PaymentMethodCode = PaymentMethodCodes.Bkash;
        order.PaymentStatus = PaymentStatus.Unpaid;

        await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Delivered });

        order.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
    }

    [Fact]
    public async Task A_return_is_recorded_on_the_goods_axis_too()
    {
        var order = GivenAnOrder(OrderStatus.Delivered);
        order.FulfilmentStatus = FulfilmentStatus.Fulfilled;

        await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Returned });

        order.FulfilmentStatus.ShouldBe(FulfilmentStatus.Returned);
    }

    [Fact]
    public async Task A_customer_hears_about_the_moves_that_concern_them()
    {
        var order = GivenAnOrder(OrderStatus.ReadyToShip);

        await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Shipped });

        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationRequest>(r =>
                r.Type == "order.status_changed"
                && r.IdempotencyKey == $"order.status:{OrderNumber}:Shipped"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task And_not_about_the_ones_that_do_not()
    {
        // "Processing" is the shop talking to itself. An SMS for every internal
        // move costs money and teaches the customer to ignore the ones that
        // matter.
        GivenAnOrder(OrderStatus.Confirmed);

        await CreateService().ChangeStatusAsync(
            OrderNumber, new ChangeOrderStatusDto { Status = OrderStatus.Processing });

        await _notifications.DidNotReceive().EnqueueAsync(
            Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // The money axis
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Staff_can_record_a_payment()
    {
        var order = GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().RecordPaymentAsync(
            OrderNumber,
            new RecordPaymentDto { Status = PaymentStatus.Paid, Note = "bKash TrxID 8HG23K" });

        result.IsSuccess.ShouldBeTrue(result.Message);
        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);

        // Same status on both ends: the order has not moved as a piece of work,
        // only the money under it.
        var entry = order.Timeline.Single();
        entry.FromStatus.ShouldBe(OrderStatus.Confirmed);
        entry.ToStatus.ShouldBe(OrderStatus.Confirmed);
        entry.Note.ShouldNotBeNull();
        entry.Note.ShouldContain("bKash TrxID 8HG23K");
        entry.Note.ShouldContain("Unpaid");
    }

    [Fact]
    public async Task A_refund_cannot_be_undone()
    {
        var order = GivenAnOrder(OrderStatus.Returned);
        order.PaymentStatus = PaymentStatus.Refunded;

        var result = await CreateService().RecordPaymentAsync(
            OrderNumber, new RecordPaymentDto { Status = PaymentStatus.Paid });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.PaymentTransitionInvalid);
        order.PaymentStatus.ShouldBe(PaymentStatus.Refunded);
    }

    // -------------------------------------------------------------------------
    // The delivery charge — the point of pricing delivery per product
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Staff_can_correct_the_delivery_charge()
    {
        // A bed and its two bedside tables price at three deliveries and go out
        // on one van with the same two men. Nobody can write that rule into a
        // rate card; the person looking at the order can see it in a second.
        var order = GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().OverrideDeliveryFeeAsync(
            OrderNumber,
            new OverrideDeliveryFeeDto
            {
                DeliveryFee = 1_600m,
                Reason = "Bed and both bedside tables go in one van."
            });

        result.IsSuccess.ShouldBeTrue(result.Message);
        order.DeliveryFee.Amount.ShouldBe(1_600m);
        order.DeliveryOverridden.ShouldBeTrue();
    }

    [Fact]
    public async Task Correcting_it_keeps_the_bill_adding_up()
    {
        // The invariant checkout established: goods net + VAT + delivery +
        // surcharge is the grand total, exactly. Only the delivery line moves —
        // VAT is charged on the goods, so it is untouched.
        var order = GivenAnOrder(OrderStatus.Confirmed);

        await CreateService().OverrideDeliveryFeeAsync(
            OrderNumber,
            new OverrideDeliveryFeeDto { DeliveryFee = 1_600m, Reason = "One van." });

        order.GrandTotal.Amount.ShouldBe(
            order.GoodsNet.Amount
            + order.VatAmount.Amount
            + order.DeliveryFee.Amount
            + order.PaymentSurcharge.Amount);

        order.VatAmount.Amount.ShouldBe(750m);
        order.GoodsNet.Amount.ShouldBe(10_000m);
    }

    [Fact]
    public async Task A_hand_set_zero_is_not_a_waiver()
    {
        // "Free because the basket passed the threshold" and "free because we
        // decided to" are different facts, and only one of them is a rule.
        var order = GivenAnOrder(OrderStatus.Confirmed);
        order.DeliveryWaived = true;

        await CreateService().OverrideDeliveryFeeAsync(
            OrderNumber,
            new OverrideDeliveryFeeDto { DeliveryFee = 0m, Reason = "Goodwill after the late delivery." });

        order.DeliveryFee.Amount.ShouldBe(0m);
        order.DeliveryWaived.ShouldBeFalse();
        order.DeliveryOverridden.ShouldBeTrue();
    }

    [Fact]
    public async Task The_change_says_what_it_was_and_what_it_became()
    {
        // The audit trail is the reason the reason is mandatory. A figure that
        // moved with no record of who moved it or why is indistinguishable from
        // a mistake when somebody reads it back.
        var order = GivenAnOrder(OrderStatus.Confirmed);

        await CreateService().OverrideDeliveryFeeAsync(
            OrderNumber,
            new OverrideDeliveryFeeDto { DeliveryFee = 1_600m, Reason = "One van." });

        var entry = order.Timeline.Single();
        entry.ActorName.ShouldBe("Rakib Hasan");
        entry.Note.ShouldNotBeNull();
        entry.Note.ShouldContain("4500");
        entry.Note.ShouldContain("1600");
        entry.Note.ShouldContain("One van.");
    }

    [Fact]
    public async Task It_must_say_why()
    {
        var order = GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().OverrideDeliveryFeeAsync(
            OrderNumber, new OverrideDeliveryFeeDto { DeliveryFee = 1_600m, Reason = "   " });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.ReasonRequired);
        order.DeliveryFee.Amount.ShouldBe(4_500m);
    }

    [Fact]
    public async Task Once_the_money_is_in_the_total_stops_moving()
    {
        // The correction from here is a refund or a second collection, both of
        // which leave a record. Editing the figure would leave the order saying
        // one thing and the till saying another.
        var order = GivenAnOrder(OrderStatus.Confirmed);
        order.PaymentStatus = PaymentStatus.Paid;

        var result = await CreateService().OverrideDeliveryFeeAsync(
            OrderNumber,
            new OverrideDeliveryFeeDto { DeliveryFee = 1_600m, Reason = "One van." });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.OrderAmountLocked);
        order.DeliveryFee.Amount.ShouldBe(4_500m);
    }

    [Fact]
    public async Task And_so_does_a_van_that_has_already_left()
    {
        // A figure raised after the goods have gone is a figure nobody is going
        // to collect.
        var order = GivenAnOrder(OrderStatus.Shipped);

        var result = await CreateService().OverrideDeliveryFeeAsync(
            OrderNumber,
            new OverrideDeliveryFeeDto { DeliveryFee = 6_000m, Reason = "Second trip needed." });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.OrderAmountLocked);
        order.DeliveryFee.Amount.ShouldBe(4_500m);
    }

    // -------------------------------------------------------------------------
    // What staff see that customers do not
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_staff_view_shows_the_phone_number_in_full()
    {
        // The customer's own view masks it. Staff need to be able to dial it,
        // which is why the two audiences have separate DTOs rather than one
        // with a flag on it.
        GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().GetAsync(OrderNumber);

        result.Data!.ContactPhone.ShouldBe("+8801712345678");
        result.Data.InternalNotes.ShouldBe("Ring before the van leaves.");
    }

    [Fact]
    public async Task The_staff_view_carries_the_moves_that_are_legal_from_here()
    {
        // Sent rather than reimplemented in Angular. A second copy of the graph
        // in TypeScript drifts, and the drift shows up as a button that renders,
        // is pressed, and returns a 409.
        GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().GetAsync(OrderNumber);

        result.Data!.AllowedStatusTransitions
            .ShouldBe([OrderStatus.Processing, OrderStatus.Cancelled]);

        result.Data.CanEditDeliveryFee.ShouldBeTrue();
    }

    [Fact]
    public async Task The_staff_view_shows_the_rate_card_beside_what_was_charged()
    {
        // The pair is what makes an override auditable: "the rate card said
        // 4,500৳, we charged 1,600৳, and the note says one van."
        GivenAnOrder(OrderStatus.Confirmed);

        var result = await CreateService().GetAsync(OrderNumber);

        result.Data!.DeliveryChargeFromLines.ShouldBe(4_500m);
        result.Data.Totals.DeliveryFee.ShouldBe(4_500m);
    }

    [Fact]
    public async Task An_order_that_does_not_exist_is_a_not_found()
    {
        _orders.GetByNumberAsync("WH-2609-99999", Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await CreateService().GetAsync("WH-2609-99999");

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.OrderNotFound);
    }

    [Fact]
    public async Task The_board_reports_a_count_for_every_status_including_the_empty_ones()
    {
        // A tab that vanishes when it reaches zero and reappears later is a
        // worse board than one with a quiet zero on it.
        _orders.CountByStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<OrderStatus, int> { [OrderStatus.Confirmed] = 3 });

        var result = await CreateService().GetStatusCountsAsync();

        result.Data!.Count.ShouldBe(Enum.GetValues<OrderStatus>().Length);
        result.Data.Single(x => x.Status == OrderStatus.Confirmed).Count.ShouldBe(3);
        result.Data.Single(x => x.Status == OrderStatus.Shipped).Count.ShouldBe(0);
    }

    [Fact]
    public async Task The_notepad_does_not_clutter_the_timeline()
    {
        // Unlike the totals, this changes nothing a customer is charged. An
        // audit row for every fixed typo would bury the entries that matter.
        var order = GivenAnOrder(OrderStatus.Confirmed);

        await CreateService().UpdateInternalNotesAsync(
            OrderNumber, new UpdateInternalNotesDto { InternalNotes = "  Second van booked.  " });

        order.InternalNotes.ShouldBe("Second van booked.");
        order.Timeline.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    /// <summary>
    /// A 10,000৳ order carrying 7.5% VAT and a 4,500৳ delivery charge that is
    /// the sum of its two lines.
    /// </summary>
    private Order GivenAnOrder(OrderStatus status)
    {
        var order = new Order
        {
            Id = 1,
            OrderNumber = OrderNumber,
            ContactName = "Rakib Hasan",
            ContactPhone = "+8801712345678",
            ShippingAddress = DeliveryAddress.Create(
                "Dhaka", "Dhaka", "House 12, Road 3", area: "Dhanmondi"),
            DeliveryZone = DeliveryZone.InsideDhaka,
            Currency = Money.Bdt,
            Subtotal = Money.Taka(10_750m),
            DiscountTotal = Money.Zero(),
            GoodsNet = Money.Taka(10_000m),
            VatAmount = Money.Taka(750m),
            VatRatePercent = 7.5m,
            PricesIncludeVat = true,
            DeliveryFee = Money.Taka(4_500m),
            PaymentSurcharge = Money.Zero(),
            GrandTotal = Money.Taka(15_250m),
            Status = status,
            PaymentStatus = PaymentStatus.Unpaid,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            PaymentMethodCode = PaymentMethodCodes.CashOnDelivery,
            PlacedAt = _clock.UtcNow,
            InternalNotes = "Ring before the van leaves.",
            Lines =
            [
                Line(1, "Segun Bed", 1, 8_000m, 2_500m),
                Line(2, "Bedside Table", 2, 1_375m, 2_000m)
            ]
        };

        _orders.GetByNumberAsync(OrderNumber, Arg.Any<CancellationToken>()).Returns(order);

        return order;
    }

    private static OrderLine Line(
        long id, string name, int quantity, decimal unitPrice, decimal deliveryCharge) =>
        new()
        {
            Id = id,
            OrderId = 1,
            ProductVariantId = id,
            ProductId = id,
            ProductNameEn = name,
            ProductSlug = name.ToLowerInvariant().Replace(' ', '-'),
            Sku = $"WH-{id:000}",
            VariantName = "Default",
            Quantity = quantity,
            UnitPrice = Money.Taka(unitPrice),
            DiscountAmount = Money.Zero(),
            LineTotal = Money.Taka(unitPrice * quantity),
            DeliveryChargeApplied = Money.Taka(deliveryCharge)
        };

    private static UserManager<AppUser> MockUserManager() =>
        Substitute.For<UserManager<AppUser>>(
            Substitute.For<IUserStore<AppUser>>(),
            null, null, null, null, null, null, null, null);
}
