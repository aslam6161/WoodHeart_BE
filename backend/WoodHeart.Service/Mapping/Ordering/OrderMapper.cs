using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Mapping.Ordering;

/// <summary>
/// Orders to the shapes the storefront renders.
/// </summary>
/// <remarks>
/// Hand-written, like the rest of the mapping here. The interesting decisions —
/// which language a name comes back in, whether the phone number is masked —
/// are exactly the ones a convention-based mapper would make silently.
/// </remarks>
public static class OrderMapper
{
    public static OrderSummaryDto ToSummary(Order order, string? language)
    {
        var first = order.Lines.FirstOrDefault();
        var extra = order.Lines.Count - 1;

        return new OrderSummaryDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            PlacedAt = order.PlacedAt,
            GrandTotal = order.GrandTotal.Amount,
            ItemCount = order.Lines.Sum(line => line.Quantity),
            PreviewImagePath = first?.ImagePath,

            // "Segun Bed and 2 more" rather than a list. A row in a history has
            // one line of space, and the order number is what the customer
            // actually recognises.
            PreviewTitle = first is null
                ? string.Empty
                : extra > 0
                    ? $"{Name(first, language)} and {extra} more"
                    : Name(first, language),

            CanCancel = OrderStatusMachine.IsCustomerCancellable(order.Status)
        };
    }

    public static OrderDetailDto ToDetail(Order order, string? language) =>
        new()
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            FulfilmentStatus = order.FulfilmentStatus,
            PaymentMethodCode = order.PaymentMethodCode,
            PlacedAt = order.PlacedAt,

            ContactName = order.ContactName,

            // Masked. The customer knows their own number, and a confirmation
            // page is a thing people screenshot and send to whoever is paying.
            ContactPhone = PhoneNumber.TryParse(order.ContactPhone, out var phone) && phone is not null
                ? phone.Masked
                : order.ContactPhone,

            ContactEmail = order.ContactEmail,
            ShippingAddress = ToAddressDto(order.ShippingAddress),
            DeliveryZone = order.DeliveryZone,
            DeliveryNote = order.DeliveryNote,
            Currency = order.Currency,

            Lines = [.. order.Lines.Select(line => ToLineDto(line, language))],

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

            CanCancel = OrderStatusMachine.IsCustomerCancellable(order.Status)
        };

    public static DeliveryAddressDto ToAddressDto(DeliveryAddress address) =>
        new()
        {
            Division = address.Division,
            District = address.District,
            Upazila = address.Upazila,
            Area = address.Area,
            AddressLine = address.AddressLine,
            Landmark = address.Landmark,
            Postcode = address.Postcode
        };

    private static OrderLineDto ToLineDto(OrderLine line, string? language) =>
        new()
        {
            Id = line.Id,
            VariantId = line.ProductVariantId,
            ProductId = line.ProductId,
            ProductName = Name(line, language),
            ProductSlug = line.ProductSlug,
            Sku = line.Sku,
            VariantName = line.VariantName,
            ImagePath = line.ImagePath,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice.Amount,
            DiscountAmount = line.DiscountAmount.Amount,
            LineTotal = line.LineTotal.Amount,
            LeadTimeDays = line.LeadTimeDays
        };

    /// <summary>
    /// The snapshotted name, in the caller's language where one was stored.
    /// </summary>
    /// <remarks>
    /// Falls back to English rather than showing an empty row: a Bangla name
    /// that was never entered must not erase the product from somebody's order
    /// history.
    /// </remarks>
    private static string Name(OrderLine line, string? language) =>
        string.Equals(language, LocalizedText.Bangla, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(line.ProductNameBn)
            ? line.ProductNameBn
            : line.ProductNameEn;
}
