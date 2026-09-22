using System.Text.Json;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Ordering;
using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.Promotions;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Interfaces.Inventory;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Interfaces.Payments;
using WoodHeart.Service.Interfaces.Promotions;
using WoodHeart.Service.Services.Common;
using WoodHeart.Service.Services.Promotions;

namespace WoodHeart.Service.Services.Ordering;

/// <summary>
/// Checkout: the one place a basket becomes money owed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything is recomputed here.</b> The client sends a contact, an address
/// and a payment method — no prices, no totals, no delivery fee. The bill is
/// rebuilt from the caller's own cart through the same <c>CartPricer</c> the
/// cart page used, so the two cannot disagree, and so a modified request cannot
/// name its own price.
/// </para>
/// <para>
/// <b>The whole placement is one transaction.</b> An order, its lines, its
/// first two timeline entries, the retirement of the cart and the outbox row
/// that becomes the customer's SMS all commit together. A half-placed order —
/// written, but with the basket still full and no confirmation sent — is worse
/// than a failed one, because nobody knows it happened.
/// </para>
/// <para>
/// <b>The delivery zone comes from the address, not from the cart.</b> The cart
/// lets a customer pick a zone so the page can quote something early; here the
/// address decides. A dropdown left on "Inside Dhaka" must not send a wardrobe
/// to Sylhet at the city rate.
/// </para>
/// </remarks>
public class CheckoutService(
    ICartRepository carts,
    IOrderRepository orders,
    IPricingContextFactory pricing,
    IPaymentProviderResolver payments,
    IPromotionService promotions,
    INumberSequenceService numbers,
    INotificationQueue notifications,
    IInventoryService inventory,
    IStoreSettingService settings,
    ICurrentUserService currentUser,
    ITokenHasher hasher,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    public async Task<GeneralResponse<IReadOnlyList<PaymentMethodDto>>> GetPaymentMethodsAsync(
        DeliveryAddressDto? address, CancellationToken cancellationToken = default)
    {
        var cart = await FindCartAsync(cancellationToken);

        if (cart is null || cart.Lines.Count == 0)
        {
            return GeneralResponse<IReadOnlyList<PaymentMethodDto>>.Fail(
                OrderingErrors.CartEmpty, "There is nothing in your basket.");
        }

        // Falls back to the cart's chosen zone while the address is still being
        // typed, so the payment step can render before the form is finished.
        var zone = address is null
            ? cart.DeliveryZone ?? DeliveryZone.InsideDhaka
            : DeliveryZoneResolver.Resolve(ToAddress(address));

        var totals = await PriceAsync(cart, zone, cancellationToken);

        var eligible = await payments.GetEligibleAsync(totals.GrandTotal, zone, cancellationToken);

        return GeneralResponse<IReadOnlyList<PaymentMethodDto>>.Success(
        [
            .. eligible.Select(method => new PaymentMethodDto
            {
                Code = method.Config.Code,
                DisplayName = method.Config.DisplayName.For(currentUser.Language),
                Description = method.Config.Description?.For(currentUser.Language),
                IconUrl = method.Config.IconUrl,
                Surcharge = method.Surcharge.Amount,
                RedirectsToGateway = method.Provider.Capabilities.SupportsRedirect
            })
        ]);
    }

    public async Task<GeneralResponse<PlacedOrderDto>> PlaceOrderAsync(
        PlaceOrderDto dto,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Before anything else: has this exact placement already happened? A
        // customer on a slow connection presses the button twice, and the
        // second press must land on the confirmation page for the order the
        // first one made — not on an error, and not on a second sofa.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await orders.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

            if (existing is not null)
            {
                return GeneralResponse<PlacedOrderDto>.Success(ToPlaced(existing, null, true));
            }
        }

        if (!PhoneNumber.TryParse(dto.ContactPhone, out var phone) || phone is null)
        {
            return Fail(OrderingErrors.ContactPhoneInvalid,
                "That does not look like a Bangladeshi mobile number.");
        }

        DeliveryAddress address;

        try
        {
            address = ToAddress(dto.ShippingAddress);
        }
        catch (ArgumentException ex)
        {
            return Fail(OrderingErrors.AddressInvalid, ex.Message);
        }

        var zone = DeliveryZoneResolver.Resolve(address);

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var cart = await FindCartAsync(ct);

            if (cart is null || cart.Lines.Count == 0)
            {
                return Fail(OrderingErrors.CartEmpty, "There is nothing in your basket.");
            }

            if (cart.Status != CartStatus.Active)
            {
                return Fail(OrderingErrors.CartNotActive, "That basket has already been checked out.");
            }

            // Refused rather than skipped. The cart page prices around a
            // withdrawn item and shows it greyed out; an order that quietly
            // dropped one would deliver two of the three things somebody paid
            // for, and they would find out at the door.
            if (cart.Lines.Any(line => !IsPurchasable(line)))
            {
                return Fail(OrderingErrors.LineNotPurchasable,
                    "Something in your basket is no longer available. Please review it.");
            }

            var undiscountedContext = await pricing.BuildAsync(zone, cart.DeliveryFeeOverride, ct);
            var priced = cart.Lines.Select(ToPricedLine).ToList();

            // Priced once without discounts, because a free-shipping discount
            // is worth exactly what delivery costs and the engine is not
            // allowed a second opinion on that figure.
            var undiscounted = CartPricer.Price(priced, undiscountedContext);

            // The same engine the basket page ran, with two things it did not
            // have: the payment method, and a phone number. Both can turn a
            // discount off — "5% with bKash" and "first order only" — so a
            // basket may legitimately be worth less here than it looked, and
            // the confirmation says what actually applied.
            var outcome = await promotions.EvaluateAsync(
                new PromotionRequest(
                    Lines: CartPromotions.LinesFrom(cart.Lines),
                    DeliveryFee: undiscounted.DeliveryFee,
                    Zone: zone,
                    PaymentMethodCode: dto.PaymentMethodCode,
                    Codes: [.. cart.Coupons.Select(coupon => coupon.Code)],
                    CustomerId: currentUser.UserId,
                    ContactPhone: phone.Value),
                ct);

            var context = PricingContextFactory.WithDiscount(
                undiscountedContext, outcome.GoodsDiscount, outcome.FreeShipping);

            var totals = CartPricer.Price(priced, context);
            var applied = CartPromotions.Settle(outcome, undiscounted, totals);

            var method = await payments.ResolveAsync(
                dto.PaymentMethodCode, totals.GrandTotal, zone, ct);

            if (method is not { } chosen)
            {
                return Fail(PaymentErrors.MethodNotAvailable,
                    "That payment method is not available for this order.");
            }

            var order = BuildOrder(cart, dto, phone, address, zone, totals, context, chosen, idempotencyKey);

            var prefix = await settings.GetStringAsync(SettingKeys.OrderNumberPrefix, ct);

            order.OrderNumber = await numbers.NextAsync(
                NumberSequenceService.Orders,
                string.IsNullOrWhiteSpace(prefix) ? GlobalConstants.DefaultOrderNumberPrefix : prefix.Trim(),
                ct);

            AddLines(order, cart, priced, context, zone, CartPromotions.PerVariant(applied));
            AddDiscounts(order, applied);

            Record(order, from: null, to: OrderStatus.Pending, "Order placed.", ct: ct);

            await orders.InsertAsync(order, ct);

            // Saved before the provider is called, so that a gateway which
            // returns a reference has an order to hang it on — and so that a
            // provider throwing takes the whole transaction down rather than
            // leaving a payment against nothing.
            await unitOfWork.SaveChangesAsync(ct);

            // Now that the order has an id. Staged in the same unit of work as
            // everything else: a redemption that committed on its own would
            // survive a placement that rolled back, and a coupon limited to a
            // hundred uses would be a coupon limited to a hundred attempts.
            await promotions.RecordUsageAsync(order, applied, ct);

            // The shelf, after the order has an id to hold against and
            // before any money moves. A refusal here rolls the order back
            // with it: an order that exists for a bed that does not is a
            // phone call the shop should not have to make. Two checkouts
            // racing for the last unit are settled by the row version on
            // the stock item — the second commit fails as a 409.
            var reserved = await inventory.ReserveForOrderAsync(order, ct);

            if (!reserved.IsSuccess)
            {
                return GeneralResponse<PlacedOrderDto>.Invalid(
                    reserved.ErrorCode ?? InventoryErrors.InsufficientStock,
                    reserved.Message,
                    reserved.Errors ?? new Dictionary<string, string[]>());
            }

            var initiated = await chosen.Provider.InitiateAsync(
                new PaymentContext(order, order.GrandTotal, dto.ReturnUrl, order.IdempotencyKey ?? order.OrderNumber),
                ct);

            if (!initiated.IsSuccess)
            {
                // Rolls the whole placement back. An order sitting in the
                // database that the customer was told had failed is the one
                // thing worse than a failed checkout.
                return Fail(
                    initiated.ErrorCode ?? PaymentErrors.Declined,
                    initiated.Message.Length > 0 ? initiated.Message : "That payment could not be started.");
            }

            var result = initiated.Data;

            ApplyPaymentOutcome(order, result.State, ct);

            // Retired, not emptied. The basket is the evidence behind the
            // order — "the customer says they ordered three chairs" needs
            // something to check against.
            cart.Status = CartStatus.CheckedOut;
            carts.Update(cart);

            await QueueConfirmationAsync(order, ct);

            await unitOfWork.SaveChangesAsync(ct);

            OrderLog.OrderPlaced(
                logger, order.OrderNumber, order.GrandTotal.Amount, order.PaymentMethodCode);

            return GeneralResponse<PlacedOrderDto>.Success(
                ToPlaced(order, result.RedirectUrl, false), id: order.Id);
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Building the order
    // -------------------------------------------------------------------------

    private Order BuildOrder(
        Cart cart,
        PlaceOrderDto dto,
        PhoneNumber phone,
        DeliveryAddress address,
        DeliveryZone zone,
        CartTotals totals,
        PricingContext context,
        EligiblePaymentMethod method,
        string? idempotencyKey) =>
        new()
        {
            CustomerId = currentUser.UserId,
            ContactName = dto.ContactName.Trim(),
            ContactPhone = phone.Value,
            ContactEmail = string.IsNullOrWhiteSpace(dto.ContactEmail) ? null : dto.ContactEmail.Trim(),
            CustomerLanguage = currentUser.Language,
            ShippingAddress = address,
            DeliveryZone = zone,
            DeliveryNote = string.IsNullOrWhiteSpace(dto.DeliveryNote) ? null : dto.DeliveryNote.Trim(),

            Currency = cart.Currency,
            Subtotal = totals.Subtotal,
            DiscountTotal = totals.DiscountTotal,
            GoodsNet = totals.GoodsNet,
            VatAmount = totals.VatAmount,
            VatRatePercent = context.VatRatePercent,
            PricesIncludeVat = context.PricesIncludeVat,
            DeliveryFee = totals.DeliveryFee,
            PaymentSurcharge = method.Surcharge,
            GrandTotal = totals.GrandTotal + method.Surcharge,
            DeliveryWaived = totals.DeliveryWaived,
            DeliveryOverridden = totals.DeliveryOverridden,

            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            PaymentMethodCode = method.Config.Code,
            PlacedAt = clock.UtcNow,
            CartId = cart.Id,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey
        };

    /// <summary>
    /// Copies each basket line onto the order, freezing everything it displays.
    /// </summary>
    /// <remarks>
    /// The product name, slug, SKU, variant name and image are all copied. A
    /// line that rendered through a live join would let a rename rewrite what an
    /// old invoice says was sold.
    /// </remarks>
    private static void AddLines(
        Order order,
        Cart cart,
        IReadOnlyList<PricedLine> priced,
        PricingContext context,
        DeliveryZone zone,
        IReadOnlyDictionary<long, Money> discountPerVariant)
    {
        var index = 0;

        foreach (var line in cart.Lines)
        {
            var variant = line.ProductVariant;
            var product = variant.Product;
            var unitPrice = variant.EffectivePrice;

            // What the engine allocated to this line, summed across every
            // discount that touched it. Stored per line so that refunding one
            // item out of four refunds what that item was actually charged.
            var discount = discountPerVariant.TryGetValue(variant.Id, out var allocated)
                ? allocated
                : Money.Zero(order.Currency);

            order.Lines.Add(new OrderLine
            {
                ProductVariantId = variant.Id,
                ProductId = product.Id,
                ProductNameEn = product.Name.En,
                ProductNameBn = product.Name.Bn,
                ProductSlug = product.Slug.Value,
                Sku = variant.Sku,
                VariantName = variant.VariantName,
                ImagePath = product.Media.FirstOrDefault(m => m.IsPrimary)?.StoragePath,
                Quantity = line.Quantity,
                UnitPrice = unitPrice,
                DiscountAmount = discount,
                LineTotal = (unitPrice.Multiply(line.Quantity) - discount).OrZeroIfNegative(),
                DeliveryChargeApplied = DeliveryPricer.ChargeForLine(
                    priced[index], zone, context.DefaultDeliveryCharge, order.Currency),
                LeadTimeDays = product.LeadTimeDays
            });

            index++;
        }
    }

    /// <summary>
    /// Copies each discount onto the order: what it was called, what was typed,
    /// and what it gave.
    /// </summary>
    /// <remarks>
    /// Snapshotted for the same reason every other display field is. A discount
    /// renamed, retargeted or archived next month must not change what an old
    /// invoice says the customer was given — and "why is this order 2,000৳ less
    /// than its lines" needs an answer that outlives the campaign.
    /// </remarks>
    private static void AddDiscounts(Order order, IReadOnlyList<AppliedDiscount> applied)
    {
        foreach (var discount in applied.Where(discount => discount.Amount.IsPositive))
        {
            order.Discounts.Add(new OrderDiscount
            {
                DiscountId = discount.DiscountId,
                Name = discount.Name,
                Code = discount.Code,
                Type = discount.Type,
                Amount = discount.Amount
            });
        }
    }

    /// <summary>
    /// Moves the order to where the payment outcome says it belongs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cash on delivery is Confirmed and Unpaid.</b> The order is accepted
    /// and the goods will ship, but no money has moved. A shop that marked it
    /// paid at placement could not answer "how much cash is out with riders",
    /// which in this market is the question that matters most.
    /// </para>
    /// <para>
    /// A gateway that is still waiting for the customer leaves the order
    /// Pending. It is confirmed when they come back and the payment executes,
    /// not before — an order confirmed on the strength of a redirect that was
    /// abandoned is a van loaded for a sale that never happened.
    /// </para>
    /// </remarks>
    private void ApplyPaymentOutcome(Order order, PaymentState state, CancellationToken ct)
    {
        switch (state)
        {
            case PaymentState.Succeeded:
                order.PaymentStatus = PaymentStatus.Paid;
                Confirm(order, "Payment received.", ct);
                break;

            case PaymentState.AwaitingCollection:
                order.PaymentStatus = PaymentStatus.Unpaid;
                Confirm(order, "Cash to be collected on delivery.", ct);
                break;

            default:
                // Pending: the customer is at the gateway. Left as placed.
                order.PaymentStatus = PaymentStatus.Unpaid;
                break;
        }
    }

    private void Confirm(Order order, string note, CancellationToken ct)
    {
        if (!OrderStatusMachine.CanTransition(order.Status, OrderStatus.Confirmed))
        {
            return;
        }

        Record(order, order.Status, OrderStatus.Confirmed, note, ct);
        order.Status = OrderStatus.Confirmed;
    }

    private void Record(
        Order order, OrderStatus? from, OrderStatus to, string note, CancellationToken ct) =>
        order.Timeline.Add(new OrderTimelineEntry
        {
            FromStatus = from,
            ToStatus = to,
            ActorUserId = currentUser.UserId,
            ActorName = currentUser.UserId is null ? "Customer" : currentUser.PhoneNumber ?? "Customer",
            Note = note,
            OccurredAt = clock.UtcNow
        });

    /// <summary>
    /// Stages the confirmation SMS and email in the same unit of work as the
    /// order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The outbox is what makes "order written, customer told" a single fact.
    /// Sending inline would either send before the commit — a confirmation for
    /// an order that then rolled back — or after it, where a crash in between
    /// loses it silently.
    /// </para>
    /// <para>
    /// Keyed on the order number so a retry of the worker cannot bill the shop
    /// twice at the SMS gateway.
    /// </para>
    /// </remarks>
    private async Task QueueConfirmationAsync(Order order, CancellationToken ct) =>
        await notifications.EnqueueAsync(
            new NotificationRequest
            {
                Type = "order.placed",
                IdempotencyKey = $"order.placed:{order.OrderNumber}",
                Payload = JsonSerializer.Serialize(new
                {
                    orderNumber = order.OrderNumber,
                    contactName = order.ContactName,
                    contactPhone = order.ContactPhone,
                    contactEmail = order.ContactEmail,
                    language = order.CustomerLanguage,
                    grandTotal = order.GrandTotal.Amount,
                    currency = order.Currency,
                    paymentMethod = order.PaymentMethodCode,
                    itemCount = order.Lines.Sum(l => l.Quantity),
                    address = order.ShippingAddress.ToSingleLine()
                })
            },
            ct);

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    /// <summary>The caller's active cart, by user id or by guest token hash.</summary>
    private async Task<Cart?> FindCartAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is { } userId)
        {
            return await carts.GetActiveForCustomerAsync(userId, cancellationToken);
        }

        var anonymousId = currentUser.AnonymousId;

        return string.IsNullOrWhiteSpace(anonymousId)
            ? null
            : await carts.GetActiveForGuestAsync(hasher.Hash(anonymousId), cancellationToken);
    }

    /// <summary>
    /// The basket priced with its discounts, for deciding which payment
    /// methods are eligible.
    /// </summary>
    /// <remarks>
    /// Evaluated with no payment method chosen, because this is the call that
    /// produces the list to choose from. A discount tied to bKash therefore
    /// counts towards the total here and is re-checked at placement — it can
    /// only ever reduce the total, so the effect is that a method with a
    /// minimum-order band is offered on the strength of a discount that then
    /// does not apply. The band is re-checked at placement, where the refusal
    /// is honest.
    /// </remarks>
    private async Task<CartTotals> PriceAsync(
        Cart cart, DeliveryZone zone, CancellationToken cancellationToken)
    {
        var context = await pricing.BuildAsync(zone, cart.DeliveryFeeOverride, cancellationToken);

        var purchasable = cart.Lines.Where(IsPurchasable).ToList();
        var priced = purchasable.Select(ToPricedLine).ToList();
        var undiscounted = CartPricer.Price(priced, context);

        var outcome = await promotions.EvaluateAsync(
            new PromotionRequest(
                Lines: CartPromotions.LinesFrom(purchasable),
                DeliveryFee: undiscounted.DeliveryFee,
                Zone: zone,
                PaymentMethodCode: null,
                Codes: [.. cart.Coupons.Select(coupon => coupon.Code)],
                CustomerId: currentUser.UserId,
                ContactPhone: currentUser.PhoneNumber),
            cancellationToken);

        return CartPricer.Price(
            priced,
            PricingContextFactory.WithDiscount(context, outcome.GoodsDiscount, outcome.FreeShipping));
    }

    private static PricedLine ToPricedLine(CartLine line) =>
        new(line.Quantity,
            line.ProductVariant.EffectivePrice,
            line.ProductVariant.Product.DeliveryChargeInsideDhaka,
            line.ProductVariant.Product.DeliveryChargeOutsideDhaka);

    private static bool IsPurchasable(CartLine line) =>
        line.ProductVariant.IsActive
        && line.ProductVariant.Product is { Status: ProductStatus.Active, IsDeleted: false };

    private static DeliveryAddress ToAddress(DeliveryAddressDto dto) =>
        DeliveryAddress.Create(
            dto.Division,
            dto.District,
            dto.AddressLine,
            dto.Upazila,
            dto.Area,
            dto.Landmark,
            dto.Postcode);

    private static PlacedOrderDto ToPlaced(Order order, string? redirectUrl, bool alreadyPlaced) =>
        new()
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            GrandTotal = order.GrandTotal.Amount,
            RedirectUrl = redirectUrl,
            AlreadyPlaced = alreadyPlaced
        };

    private static GeneralResponse<PlacedOrderDto> Fail(string code, string message) =>
        GeneralResponse<PlacedOrderDto>.Fail(code, message);
}
