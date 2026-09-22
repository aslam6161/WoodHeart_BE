using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.Promotions;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Tests.Promotions;

/// <summary>
/// The discount engine.
/// </summary>
/// <remarks>
/// PLAN.md §12 names three places money is lost silently: this, the checkout
/// total, and stock reservation. So this suite is exhaustive by intent rather
/// than by coverage target — every condition, every refusal, and the two things
/// that are easy to get subtly wrong: how amounts are capped, and how they are
/// allocated back onto the lines.
/// </remarks>
public class DiscountEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    // /1/ is Furniture, /1/2/ Bedroom, /1/2/3/ Beds, /1/4/ Living.
    private const string Beds = "/1/2/3/";
    private const string Sofas = "/1/4/9/";

    // -------------------------------------------------------------------------
    // The ordinary cases
    // -------------------------------------------------------------------------

    [Fact]
    public void No_discounts_means_no_discount()
    {
        var outcome = DiscountEngine.Evaluate([], Context([Bed(50_000m)]));

        outcome.Applied.ShouldBeEmpty();
        outcome.GoodsDiscount.ShouldBe(Money.Zero());
        outcome.FreeShipping.ShouldBeFalse();
    }

    [Fact]
    public void An_automatic_percentage_applies_without_anybody_typing_anything()
    {
        var sale = Discount("September sale", type: DiscountType.Percentage, value: 10m);

        var outcome = DiscountEngine.Evaluate([sale], Context([Bed(50_000m)]));

        outcome.GoodsDiscount.ShouldBe(Money.Taka(5_000m));
        outcome.Applied.ShouldHaveSingleItem().Name.ShouldBe("September sale");
    }

    [Fact]
    public void A_ceiling_caps_the_damage_on_a_percentage()
    {
        // The reason MaxDiscountAmount exists: 20% written for a 5,000৳ chair,
        // applied to the most expensive thing in the catalogue.
        var sale = Discount(
            type: DiscountType.Percentage, value: 20m, maxDiscount: Money.Taka(3_000m));

        var outcome = DiscountEngine.Evaluate([sale], Context([Bed(70_000m)]));

        outcome.GoodsDiscount.ShouldBe(Money.Taka(3_000m));
    }

    [Fact]
    public void A_fixed_amount_never_exceeds_what_it_applies_to()
    {
        // A 5,000৳ coupon on a 3,000৳ chair makes the chair free. It does not
        // start paying for the sofa beside it.
        var coupon = Discount(
            code: "TAKA5000",
            type: DiscountType.FixedAmount,
            value: 5_000m,
            targets: [ProductTarget(7)]);

        var outcome = DiscountEngine.Evaluate(
            [coupon],
            Context([Chair(3_000m), Bed(50_000m)], codes: ["TAKA5000"]));

        outcome.GoodsDiscount.ShouldBe(Money.Taka(3_000m));
    }

    [Fact]
    public void Free_shipping_is_worth_the_delivery_charge_and_touches_nothing_else()
    {
        var coupon = Discount(code: "FREEDEL", type: DiscountType.FreeShipping);

        var outcome = DiscountEngine.Evaluate(
            [coupon],
            Context([Bed(50_000m)], codes: ["FREEDEL"], deliveryFee: Money.Taka(1_500m)));

        outcome.FreeShipping.ShouldBeTrue();

        // Not money off the goods: it must not reduce the VAT base.
        outcome.GoodsDiscount.ShouldBe(Money.Zero());
        outcome.Applied.ShouldHaveSingleItem().Amount.ShouldBe(Money.Taka(1_500m));
    }

    // -------------------------------------------------------------------------
    // Coupons
    // -------------------------------------------------------------------------

    [Fact]
    public void A_coupon_does_nothing_until_it_is_typed()
    {
        var coupon = Discount(code: "EID25", value: 25m);

        DiscountEngine.Evaluate([coupon], Context([Bed(50_000m)]))
            .GoodsDiscount.ShouldBe(Money.Zero());

        DiscountEngine.Evaluate([coupon], Context([Bed(50_000m)], codes: ["EID25"]))
            .GoodsDiscount.ShouldBe(Money.Taka(12_500m));
    }

    [Fact]
    public void A_typed_code_is_matched_whatever_case_it_was_typed_in()
    {
        var coupon = Discount(code: "EID25", value: 25m);

        DiscountEngine.Evaluate([coupon], Context([Bed(50_000m)], codes: ["eid25"]))
            .GoodsDiscount.ShouldBe(Money.Taka(12_500m));
    }

    [Fact]
    public void A_code_nobody_has_heard_of_is_refused_as_such()
    {
        var outcome = DiscountEngine.Evaluate([], Context([Bed(50_000m)], codes: ["NOPE"]));

        outcome.Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponNotFound);
    }

    [Theory]
    [InlineData(DiscountStatus.Draft)]
    [InlineData(DiscountStatus.Paused)]
    [InlineData(DiscountStatus.Archived)]
    public void A_code_that_is_not_active_says_so_rather_than_saying_nothing(DiscountStatus status)
    {
        var coupon = Discount(code: "EID25", status: status);

        var outcome = DiscountEngine.Evaluate([coupon], Context([Bed(50_000m)], codes: ["EID25"]));

        outcome.Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponInactive);
    }

    [Fact]
    public void A_window_that_has_not_opened_is_distinguished_from_one_that_has_closed()
    {
        var early = Discount(code: "OCT", startsAt: Now.AddDays(9));
        var late = Discount(code: "AUG", endsAt: Now.AddDays(-1));

        DiscountEngine.Evaluate([early], Context([Bed(50_000m)], codes: ["OCT"]))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponNotStarted);

        DiscountEngine.Evaluate([late], Context([Bed(50_000m)], codes: ["AUG"]))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponExpired);
    }

    [Fact]
    public void The_end_of_a_window_is_exclusive()
    {
        // Ends at midnight means it does not apply at midnight. A discount
        // "until the 30th" is set to end at the start of the 1st.
        var coupon = Discount(code: "EID25", endsAt: Now);

        DiscountEngine.Evaluate([coupon], Context([Bed(50_000m)], codes: ["EID25"]))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponExpired);
    }

    // -------------------------------------------------------------------------
    // Conditions
    // -------------------------------------------------------------------------

    [Fact]
    public void A_minimum_spend_is_measured_on_what_the_discount_applies_to()
    {
        var coupon = Discount(code: "SPEND", minSubtotal: Money.Taka(20_000m), targets: [CategoryTarget(Sofas)]);

        // A bed does not qualify somebody for a sofa discount, however
        // expensive the bed is.
        DiscountEngine.Evaluate([coupon], Context([Bed(50_000m), Sofa(15_000m)], codes: ["SPEND"]))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponMinSubtotal);

        DiscountEngine.Evaluate([coupon], Context([Bed(50_000m), Sofa(25_000m)], codes: ["SPEND"]))
            .GoodsDiscount.ShouldBe(Money.Taka(2_500m));
    }

    [Fact]
    public void A_minimum_quantity_counts_the_items_it_applies_to()
    {
        var coupon = Discount(code: "THREE", minQuantity: 3, targets: [CategoryTarget(Sofas)]);

        DiscountEngine.Evaluate([coupon], Context([Sofa(9_000m, quantity: 2)], codes: ["THREE"]))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponMinQuantity);

        DiscountEngine.Evaluate([coupon], Context([Sofa(9_000m, quantity: 3)], codes: ["THREE"]))
            .Applied.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_category_target_reaches_everything_beneath_it()
    {
        // "/1/2/" is Bedroom; the bed is filed under Bedroom → Beds. A
        // discount on Bedroom that missed the beds would be the whole point of
        // a category tree, wasted.
        var coupon = Discount(code: "BEDROOM", targets: [CategoryTarget("/1/2/")]);

        var outcome = DiscountEngine.Evaluate(
            [coupon], Context([Bed(50_000m), Sofa(30_000m)], codes: ["BEDROOM"]));

        outcome.GoodsDiscount.ShouldBe(Money.Taka(5_000m));
    }

    [Fact]
    public void A_target_that_matches_nothing_in_the_basket_says_so()
    {
        var coupon = Discount(code: "SOFAS", targets: [CategoryTarget(Sofas)]);

        DiscountEngine.Evaluate([coupon], Context([Bed(50_000m)], codes: ["SOFAS"]))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponNoEligibleItems);
    }

    [Fact]
    public void A_zone_it_is_not_offered_in_refuses_it()
    {
        var coupon = Discount(code: "DHAKA", zones: [DeliveryZone.InsideDhaka]);

        DiscountEngine.Evaluate(
                [coupon], Context([Bed(50_000m)], codes: ["DHAKA"], zone: DeliveryZone.OutsideDhaka))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponZone);

        // On the basket page, before a zone has been chosen, it still shows.
        // Hiding it until the customer types an address would hide the reason
        // to type one.
        DiscountEngine.Evaluate([coupon], Context([Bed(50_000m)], codes: ["DHAKA"], zone: null))
            .Applied.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_payment_method_it_is_tied_to_refuses_the_others()
    {
        var coupon = Discount(code: "BKASH5", methods: ["bkash"]);

        DiscountEngine.Evaluate(
                [coupon], Context([Bed(50_000m)], codes: ["BKASH5"], paymentMethod: "cod"))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponPaymentMethod);

        DiscountEngine.Evaluate(
                [coupon], Context([Bed(50_000m)], codes: ["BKASH5"], paymentMethod: "bkash"))
            .Applied.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_first_order_discount_is_refused_to_somebody_who_has_ordered_before()
    {
        var coupon = Discount(code: "WELCOME", firstOrderOnly: true);

        DiscountEngine.Evaluate(
                [coupon], Context([Bed(50_000m)], codes: ["WELCOME"], isFirstOrder: false))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponFirstOrderOnly);
    }

    [Fact]
    public void Usage_limits_are_enforced_in_total_and_per_customer()
    {
        var total = Discount(code: "HUNDRED", usageLimitTotal: 100);
        var perCustomer = Discount(code: "ONCE", usageLimitPerCustomer: 1);

        DiscountEngine.Evaluate(
                [total],
                Context([Bed(50_000m)], codes: ["HUNDRED"], usage: Used(total.Id, 100, 0)))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponLimitReached);

        DiscountEngine.Evaluate(
                [perCustomer],
                Context([Bed(50_000m)], codes: ["ONCE"], usage: Used(perCustomer.Id, 40, 1)))
            .Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponCustomerLimitReached);
    }

    // -------------------------------------------------------------------------
    // Resolution between discounts
    // -------------------------------------------------------------------------

    [Fact]
    public void Two_stackable_discounts_both_apply_and_neither_compounds_the_other()
    {
        // 10% of 50,000 plus 500 off. Not 10% of 49,500 — an order whose total
        // depends on which discount was evaluated first is one nobody can
        // explain to a customer.
        var percentage = Discount("Sale", value: 10m, stackable: true);
        var fixedAmount = Discount(
            "Voucher", type: DiscountType.FixedAmount, value: 500m, stackable: true, id: 2);

        var outcome = DiscountEngine.Evaluate([percentage, fixedAmount], Context([Bed(50_000m)]));

        outcome.Applied.Count.ShouldBe(2);
        outcome.GoodsDiscount.ShouldBe(Money.Taka(5_500m));
    }

    [Fact]
    public void A_discount_that_does_not_stack_takes_the_basket_alone()
    {
        var exclusive = Discount("Clearance", value: 20m, priority: 10);
        var other = Discount("Sale", value: 10m, stackable: true, id: 2);

        var outcome = DiscountEngine.Evaluate([exclusive, other], Context([Bed(50_000m)]));

        outcome.Applied.ShouldHaveSingleItem().Name.ShouldBe("Clearance");
        outcome.GoodsDiscount.ShouldBe(Money.Taka(10_000m));
    }

    [Fact]
    public void Priority_decides_before_value_does()
    {
        // The shop's ordering wins over the customer's arithmetic. A campaign
        // marked as the one to run is the one that runs, even when another is
        // worth more.
        var preferred = Discount("Campaign", value: 5m, priority: 10);
        var richer = Discount("Clearance", value: 20m, priority: 0, id: 2);

        var outcome = DiscountEngine.Evaluate([preferred, richer], Context([Bed(50_000m)]));

        outcome.Applied.ShouldHaveSingleItem().Name.ShouldBe("Campaign");
    }

    [Fact]
    public void Between_equals_the_customer_gets_the_better_one()
    {
        var smaller = Discount("Five", value: 5m);
        var larger = Discount("Twenty", value: 20m, id: 2);

        DiscountEngine.Evaluate([smaller, larger], Context([Bed(50_000m)]))
            .Applied.ShouldHaveSingleItem().Name.ShouldBe("Twenty");
    }

    [Fact]
    public void A_coupon_blocked_by_something_better_is_told_why()
    {
        var automatic = Discount("Clearance", value: 20m, priority: 5);
        var coupon = Discount(code: "EID10", value: 10m, id: 2);

        var outcome = DiscountEngine.Evaluate(
            [automatic, coupon], Context([Bed(50_000m)], codes: ["EID10"]));

        outcome.Applied.ShouldHaveSingleItem().Name.ShouldBe("Clearance");
        outcome.Rejected.ShouldHaveSingleItem().Reason.ShouldBe(PromotionErrors.CouponNotCombinable);
    }

    [Fact]
    public void The_same_basket_always_produces_the_same_bill()
    {
        // Two identical discounts. Ties break on the id, so the result does not
        // depend on the order the database happened to return them in.
        var first = Discount("A", value: 10m, id: 1);
        var second = Discount("B", value: 10m, id: 2);

        DiscountEngine.Evaluate([first, second], Context([Bed(50_000m)]))
            .Applied.ShouldHaveSingleItem().Name.ShouldBe("A");

        DiscountEngine.Evaluate([second, first], Context([Bed(50_000m)]))
            .Applied.ShouldHaveSingleItem().Name.ShouldBe("A");
    }

    [Fact]
    public void Discounts_together_never_exceed_the_basket()
    {
        var first = Discount("A", type: DiscountType.FixedAmount, value: 4_000m, stackable: true, id: 1);
        var second = Discount("B", type: DiscountType.FixedAmount, value: 4_000m, stackable: true, id: 2);

        var outcome = DiscountEngine.Evaluate([first, second], Context([Chair(5_000m)]));

        // The goods are free; the shop does not owe the customer money.
        outcome.GoodsDiscount.ShouldBe(Money.Taka(5_000m));

        // And the recorded amounts sum to what was actually given, so the
        // usage report is not 3,000৳ out.
        outcome.Applied.Sum(discount => discount.Amount.Amount).ShouldBe(5_000m);
    }

    // -------------------------------------------------------------------------
    // Allocation onto the lines
    // -------------------------------------------------------------------------

    [Fact]
    public void A_discount_is_spread_across_the_lines_it_applies_to_in_proportion()
    {
        var sale = Discount(value: 10m);

        var outcome = DiscountEngine.Evaluate(
            [sale], Context([Bed(60_000m), Chair(40_000m)]));

        var applied = outcome.Applied.ShouldHaveSingleItem();

        applied.PerVariant[1].ShouldBe(Money.Taka(6_000m));
        applied.PerVariant[2].ShouldBe(Money.Taka(4_000m));
    }

    [Fact]
    public void The_shares_add_up_exactly_even_when_the_split_does_not_divide()
    {
        // Three equal lines and a 10৳ discount: 3.33 each leaves a poisha
        // unaccounted for. An invoice whose lines do not sum to its own total
        // is the kind of thing an auditor stops on, so the largest line
        // absorbs the remainder.
        var coupon = Discount(code: "TEN", type: DiscountType.FixedAmount, value: 10m);

        var outcome = DiscountEngine.Evaluate(
            [coupon],
            Context([Chair(100m, variantId: 1), Chair(100m, variantId: 2), Chair(101m, variantId: 3)],
                codes: ["TEN"]));

        var applied = outcome.Applied.ShouldHaveSingleItem();

        applied.PerVariant.Values.Sum(share => share.Amount).ShouldBe(applied.Amount.Amount);
        applied.Amount.ShouldBe(Money.Taka(10m));
    }

    [Fact]
    public void A_targeted_discount_allocates_only_to_the_lines_it_targets()
    {
        var coupon = Discount(code: "SOFAS", value: 10m, targets: [CategoryTarget(Sofas)]);

        var outcome = DiscountEngine.Evaluate(
            [coupon], Context([Bed(50_000m), Sofa(30_000m, variantId: 2)], codes: ["SOFAS"]));

        var applied = outcome.Applied.ShouldHaveSingleItem();

        applied.PerVariant.ContainsKey(1).ShouldBeFalse();
        applied.PerVariant[2].ShouldBe(Money.Taka(3_000m));
    }

    [Fact]
    public void Free_shipping_allocates_nothing_to_any_line()
    {
        // It comes off the delivery line, not off any product. Allocating it
        // would put a discount on a sofa that the sofa never received.
        var coupon = Discount(code: "FREEDEL", type: DiscountType.FreeShipping);

        DiscountEngine.Evaluate(
                [coupon], Context([Bed(50_000m)], codes: ["FREEDEL"], deliveryFee: Money.Taka(1_500m)))
            .Applied.ShouldHaveSingleItem().PerVariant.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static DiscountContext Context(
        IReadOnlyList<DiscountLine> lines,
        IReadOnlyCollection<string>? codes = null,
        Money? deliveryFee = null,
        DeliveryZone? zone = DeliveryZone.InsideDhaka,
        string? paymentMethod = null,
        bool isFirstOrder = true,
        IReadOnlyDictionary<long, DiscountUsage>? usage = null) =>
        new(
            Lines: lines,
            Now: Now,
            DeliveryFee: deliveryFee ?? Money.Zero(),
            Zone: zone,
            PaymentMethodCode: paymentMethod,
            Codes: codes,
            IsFirstOrder: isFirstOrder,
            Usage: usage);

    private static Dictionary<long, DiscountUsage> Used(long discountId, int total, int byCustomer) =>
        new() { [discountId] = new DiscountUsage(total, byCustomer) };

    private static DiscountLine Bed(decimal amount, int quantity = 1, long variantId = 1) =>
        new(variantId, ProductId: 1, CategoryPath: Beds, quantity, Money.Taka(amount));

    private static DiscountLine Sofa(decimal amount, int quantity = 1, long variantId = 2) =>
        new(variantId, ProductId: 2, CategoryPath: Sofas, quantity, Money.Taka(amount));

    private static DiscountLine Chair(decimal amount, int quantity = 1, long variantId = 2) =>
        new(variantId, ProductId: 7, CategoryPath: Sofas, quantity, Money.Taka(amount));

    private static DiscountTarget CategoryTarget(string materializedPath) =>
        new() { CategoryId = 1, Category = new Category { MaterializedPath = materializedPath } };

    private static DiscountTarget ProductTarget(long productId) => new() { ProductId = productId };

    private static Discount Discount(
        string name = "Discount",
        string? code = null,
        DiscountType type = DiscountType.Percentage,
        decimal value = 10m,
        Money? maxDiscount = null,
        Money? minSubtotal = null,
        int? minQuantity = null,
        bool firstOrderOnly = false,
        IReadOnlyList<DeliveryZone>? zones = null,
        IReadOnlyList<string>? methods = null,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        int? usageLimitTotal = null,
        int? usageLimitPerCustomer = null,
        bool stackable = false,
        int priority = 0,
        DiscountStatus status = DiscountStatus.Active,
        IReadOnlyList<DiscountTarget>? targets = null,
        long id = 1) =>
        new()
        {
            Id = id,
            Name = name,
            Code = code,
            Type = type,
            Value = value,
            MaxDiscountAmount = maxDiscount,
            MinSubtotal = minSubtotal,
            MinQuantity = minQuantity,
            FirstOrderOnly = firstOrderOnly,
            DeliveryZones = [.. zones ?? []],
            PaymentMethods = [.. methods ?? []],
            StartsAt = startsAt,
            EndsAt = endsAt,
            UsageLimitTotal = usageLimitTotal,
            UsageLimitPerCustomer = usageLimitPerCustomer,
            Stackable = stackable,
            Priority = priority,
            Status = status,
            Targets = [.. targets ?? []]
        };
}
