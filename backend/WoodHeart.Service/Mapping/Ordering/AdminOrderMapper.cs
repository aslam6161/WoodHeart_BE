using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Ordering;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Mapping.Ordering;

/// <summary>
/// Orders to the shapes the admin panel renders.
/// </summary>
/// <remarks>
/// <para>
/// Its own mapper rather than a flag on <see cref="OrderMapper"/>. The
/// difference between the two is which fields a person is allowed to see, and a
/// <c>bool isStaff</c> threaded through one mapper is a single wrong argument
/// away from putting an internal note on a customer's confirmation page.
/// </para>
/// <para>
/// Names come back in English here regardless of the caller's language header:
/// the staff screens are English, and an order board that switches to Bangla
/// because a browser asked politely is a support call.
/// </para>
/// </remarks>
public static class AdminOrderMapper
{
    public static AdminOrderSummaryDto ToSummary(Order order) =>
        new()
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            PlacedAt = order.PlacedAt,
            ContactName = order.ContactName,

            // Unmasked, on purpose. The first thing anybody does with a new
            // order is ring the customer about it.
            ContactPhone = order.ContactPhone,

            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            FulfilmentStatus = order.FulfilmentStatus,
            PaymentMethodCode = order.PaymentMethodCode,
            DeliveryZone = order.DeliveryZone,

            // Narrowest thing on the address that is still recognisable —
            // "Dhanmondi" tells a dispatcher more than "Dhaka" does.
            ShippingArea = order.ShippingAddress.Area
                           ?? order.ShippingAddress.Upazila
                           ?? order.ShippingAddress.District,

            GrandTotal = order.GrandTotal.Amount,
            DeliveryFee = order.DeliveryFee.Amount,
            DeliveryOverridden = order.DeliveryOverridden,
            ItemCount = order.Lines.Sum(line => line.Quantity),
            HasAccount = order.CustomerId is not null
        };

    public static AdminOrderDetailDto ToDetail(Order order) =>
        new()
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            PlacedAt = order.PlacedAt,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            FulfilmentStatus = order.FulfilmentStatus,
            PaymentMethodCode = order.PaymentMethodCode,

            // Sent from the same table the API validates against, so a button
            // that renders is a button that works.
            AllowedStatusTransitions = OrderStatusMachine.NextFrom(order.Status),
            AllowedPaymentTransitions = PaymentStatusMachine.NextFrom(order.PaymentStatus),
            CanEditDeliveryFee = CanEditDeliveryFee(order),

            CustomerId = order.CustomerId,
            ContactName = order.ContactName,
            ContactPhone = order.ContactPhone,
            ContactEmail = order.ContactEmail,
            ShippingAddress = OrderMapper.ToAddressDto(order.ShippingAddress),
            ShippingAddressLine = order.ShippingAddress.ToSingleLine(),
            DeliveryZone = order.DeliveryZone,
            DeliveryNote = order.DeliveryNote,
            InternalNotes = order.InternalNotes,
            Currency = order.Currency,

            Lines = [.. order.Lines.Select(ToLineDto)],

            Totals = new OrderTotalsDto
            {
                Subtotal = order.Subtotal.Amount,
                DiscountTotal = order.DiscountTotal.Amount,
                GoodsNet = order.GoodsNet.Amount,
                VatAmount = order.VatAmount.Amount,
                VatRatePercent = order.VatRatePercent,
                PricesIncludeVat = order.PricesIncludeVat,
                DeliveryFee = order.DeliveryFee.Amount,
                PaymentSurcharge = order.PaymentSurcharge.Amount,
                GrandTotal = order.GrandTotal.Amount,
                DeliveryWaived = order.DeliveryWaived,
                DeliveryOverridden = order.DeliveryOverridden
            },

            // What the rate card said at the time, against what was charged.
            DeliveryChargeFromLines = order.Lines.Sum(line => line.DeliveryChargeApplied.Amount),

            Timeline =
            [
                .. order.Timeline.Select(entry => new OrderTimelineEntryDto
                {
                    FromStatus = entry.FromStatus,
                    ToStatus = entry.ToStatus,
                    ActorName = entry.ActorName,
                    Note = entry.Note,
                    OccurredAt = entry.OccurredAt
                })
            ],

            CartId = order.CartId
        };

    /// <summary>
    /// Whether the delivery charge — and therefore the total — may still move.
    /// </summary>
    /// <remarks>
    /// Two independent locks. Money already collected is the stronger one; the
    /// goods having left is the practical one, because a figure raised after the
    /// van has gone is a figure nobody is going to collect.
    /// </remarks>
    public static bool CanEditDeliveryFee(Order order) =>
        PaymentStatusMachine.IsAmountStillEditable(order.PaymentStatus)
        && order.Status is not (OrderStatus.Shipped
            or OrderStatus.Delivered
            or OrderStatus.Completed
            or OrderStatus.Cancelled
            or OrderStatus.Returned
            or OrderStatus.Refunded);

    private static AdminOrderLineDto ToLineDto(OrderLine line) =>
        new()
        {
            Id = line.Id,
            VariantId = line.ProductVariantId,
            ProductId = line.ProductId,
            ProductName = line.ProductNameEn,
            ProductSlug = line.ProductSlug,
            Sku = line.Sku,
            VariantName = line.VariantName,
            ImagePath = line.ImagePath,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice.Amount,
            DiscountAmount = line.DiscountAmount.Amount,
            LineTotal = line.LineTotal.Amount,
            DeliveryChargeApplied = line.DeliveryChargeApplied.Amount,
            LeadTimeDays = line.LeadTimeDays
        };
}
