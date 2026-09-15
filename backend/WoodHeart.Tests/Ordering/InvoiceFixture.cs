using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Infrastructure.Documents;
using WoodHeart.Service.Mapping.Ordering;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// The order the invoice tests draw.
/// </summary>
/// <remarks>
/// The same figures as <see cref="AdminOrderServiceTests"/>: 10,000৳ of goods
/// with 750৳ of VAT inside them at 7.5%, and 4,500৳ of delivery from a bed and
/// two bedside tables. Sharing the arithmetic across the two files means an
/// invoice test and an order test cannot quietly disagree about what a correct
/// order looks like.
/// </remarks>
internal static class InvoiceFixture
{
    public const string OrderNumber = "WH-2609-00042";

    /// <summary>
    /// Renders strictly, for the whole test assembly.
    /// </summary>
    /// <remarks>
    /// QuestPDF draws a placeholder box for a character its fonts cannot
    /// supply, and only throws when this flag is on — which by default is only
    /// under a debugger. Production keeps the lenient behaviour, because a
    /// customer's emoji in an address line must not fail the request that was
    /// going to produce their invoice. The tests want the opposite: a font gap
    /// is exactly the failure that would otherwise reach the printer.
    /// </remarks>
    static InvoiceFixture()
    {
        InvoiceRenderer.Prepare();

        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
    }

    public static Order Order(
        PaymentStatus paymentStatus = PaymentStatus.Unpaid,
        string contactName = "Rakib Hasan",
        DeliveryAddress? address = null,
        IEnumerable<OrderLine>? lines = null) =>
        new()
        {
            Id = 1,
            OrderNumber = OrderNumber,
            ContactName = contactName,
            ContactPhone = "+8801712345678",
            ContactEmail = "rakib@example.com",
            CustomerLanguage = GlobalConstants.DefaultLanguage,
            ShippingAddress = address ?? DeliveryAddress.Create(
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
            Status = OrderStatus.Confirmed,
            PaymentStatus = paymentStatus,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            PaymentMethodCode = PaymentMethodCodes.CashOnDelivery,
            PlacedAt = FakeClock.DefaultNow,
            InternalNotes = "Ring before the van leaves.",
            Lines = [.. lines ?? [Line(1, "Segun Bed", 1, 8_000m), Line(2, "Bedside Table", 2, 1_375m)]]
        };

    public static OrderLine Line(long id, string name, int quantity, decimal unitPrice) =>
        new()
        {
            Id = id,
            ProductVariantId = id,
            ProductId = id,
            ProductNameEn = name,
            ProductSlug = name.ToLowerInvariant().Replace(' ', '-'),
            Sku = $"WH-{id:D4}",
            VariantName = "Segun · 6ft",
            Quantity = quantity,
            UnitPrice = Money.Taka(unitPrice),
            DiscountAmount = Money.Zero(),
            LineTotal = Money.Taka(unitPrice * quantity),
            DeliveryChargeApplied = Money.Taka(1_500m)
        };

    public static InvoiceShop Shop() =>
        new()
        {
            Name = "WoodHeart",
            AddressLine = "House 7, Road 11, Banani, Dhaka 1213",
            Phone = "01712345678",
            Email = "hello@woodheart.com.bd",
            Bin = "004071234-0101"
        };

    public static InvoiceModel Model(Order? order = null, InvoiceShop? shop = null) =>
        InvoiceMapper.ToModel(
            order ?? Order(),
            shop ?? Shop(),
            FakeClock.DefaultNow,
            FakeClock.DefaultNow.AddDays(3),
            "Cash on delivery");
}
