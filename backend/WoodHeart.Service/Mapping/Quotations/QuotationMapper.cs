using WoodHeart.Domain.Entity.Quotations;
using WoodHeart.Domain.Quotations;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Service.DTOs.Quotations;
using WoodHeart.Service.Mapping.Ordering;

namespace WoodHeart.Service.Mapping.Quotations;

/// <summary>
/// Quotations to the shapes the screens render.
/// </summary>
/// <remarks>
/// The decision worth seeing: the customer's copy and the shop's are the same
/// DTO with two fields withheld. Internal notes are the designer's own —
/// "she will go to 80 if pushed" — and a masked phone is what belongs on a
/// page people screenshot.
/// </remarks>
public static class QuotationMapper
{
    public static QuotationDto ToDto(Quotation quotation, DateOnly todayInDhaka, bool forStaff) =>
        new()
        {
            Id = quotation.Id,
            QuotationNumber = quotation.QuotationNumber,
            BookingNumber = quotation.Booking?.BookingNumber,
            ContactName = quotation.ContactName,
            ContactPhone = forStaff ? quotation.ContactPhone : Mask(quotation.ContactPhone),
            ContactEmail = quotation.ContactEmail,
            ShippingAddress = quotation.ShippingAddress is null
                ? null
                : OrderMapper.ToAddressDto(quotation.ShippingAddress),
            Status = quotation.Status,
            ValidUntil = quotation.ValidUntil,
            HasLapsed = QuotationStatusMachine.HasLapsed(
                quotation.Status, quotation.ValidUntil, todayInDhaka),
            SentAt = quotation.SentAt,
            RespondedAt = quotation.RespondedAt,
            DeclineReason = quotation.DeclineReason,
            Notes = quotation.Notes,

            // The designer's own margin notes. Absent from the customer's copy
            // entirely rather than blanked, so nothing on the wire hints there
            // is something being withheld.
            InternalNotes = forStaff ? quotation.InternalNotes : null,

            Subtotal = quotation.Subtotal.Amount,
            DiscountTotal = quotation.DiscountTotal.Amount,
            GoodsNet = quotation.GoodsNet.Amount,
            VatAmount = quotation.VatAmount.Amount,
            VatRatePercent = quotation.VatRatePercent,
            PricesIncludeVat = quotation.PricesIncludeVat,
            DeliveryFee = quotation.DeliveryFee.Amount,
            DeliveryOverridden = quotation.DeliveryFeeOverride is not null,
            GrandTotal = quotation.GrandTotal.Amount,
            OrderNumber = quotation.ConvertedOrder?.OrderNumber,

            Lines =
            [
                .. quotation.Lines
                    .OrderBy(line => line.SortOrder)
                    .ThenBy(line => line.Id)
                    .Select(ToLineDto)
            ],

            CanAnswer = QuotationStatusMachine.IsAnswerable(quotation.Status)
                        && !QuotationStatusMachine.HasLapsed(
                            quotation.Status, quotation.ValidUntil, todayInDhaka),

            // Only the shop draws buttons from this; the customer's two are
            // Accept and Decline, which CanAnswer already says.
            AllowedStatusTransitions = forStaff
                ? QuotationStatusMachine.NextFrom(quotation.Status)
                : []
        };

    public static QuotationListItemDto ToListItem(Quotation quotation, DateOnly todayInDhaka) =>
        new()
        {
            Id = quotation.Id,
            QuotationNumber = quotation.QuotationNumber,
            ContactName = quotation.ContactName,
            ContactPhone = quotation.ContactPhone,
            Status = quotation.Status,
            HasLapsed = QuotationStatusMachine.HasLapsed(
                quotation.Status, quotation.ValidUntil, todayInDhaka),
            ValidUntil = quotation.ValidUntil,
            GrandTotal = quotation.GrandTotal.Amount,
            LineCount = quotation.Lines.Count,
            BookingNumber = quotation.Booking?.BookingNumber,
            OrderNumber = quotation.ConvertedOrder?.OrderNumber,
            CreatedAt = quotation.CreatedAt
        };

    private static QuotationLineDto ToLineDto(QuotationLine line) =>
        new()
        {
            Id = line.Id,
            ProductVariantId = line.ProductVariantId,
            Description = line.Description,
            VariantName = line.VariantName,
            Sku = line.Sku,
            ImagePath = line.ImagePath,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice.Amount,
            LineTotal = line.LineTotal.Amount,
            LeadTimeDays = line.LeadTimeDays,
            HoldsStock = line.HoldsStock
        };

    private static string Mask(string phone) =>
        PhoneNumber.TryParse(phone, out var parsed) && parsed is not null ? parsed.Masked : phone;
}
