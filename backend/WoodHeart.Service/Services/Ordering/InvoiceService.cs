using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Repository.Interfaces.Payments;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Infrastructure.Documents;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Mapping.Ordering;

namespace WoodHeart.Service.Services.Ordering;

/// <summary>
/// The invoice for an order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is stored.</b> The PDF is drawn on request and thrown away. It is
/// a rendering of the order rather than a document in its own right, and the
/// order is already immutable in every respect an invoice reports — so a saved
/// copy would only be a second thing to back up, and a second thing that could
/// disagree with the first.
/// </para>
/// <para>
/// The shop's own details are the exception: they are read live rather than
/// snapshotted, so a reprint carries the address a customer should post a
/// return to today, not the one the shop had when the order was placed.
/// </para>
/// </remarks>
public class InvoiceService(
    IOrderRepository orders,
    IPaymentMethodConfigRepository paymentMethods,
    IStoreSettingService settings,
    IDateTimeProvider clock,
    ILogger<InvoiceService> logger) : IInvoiceService
{
    /// <summary>What the header says when nobody has set <c>store.name</c>.</summary>
    private const string FallbackShopName = "WoodHeart";

    public async Task<GeneralResponse<InvoiceFile>> RenderAsync(
        string orderNumber, CancellationToken cancellationToken = default)
    {
        var trimmed = orderNumber?.Trim() ?? string.Empty;

        var order = trimmed.Length == 0
            ? null
            : await orders.GetByNumberAsync(trimmed, cancellationToken);

        if (order is null)
        {
            return GeneralResponse<InvoiceFile>.Fail(
                OrderingErrors.OrderNotFound, "That order could not be found.");
        }

        var model = InvoiceMapper.ToModel(
            order,
            await ShopAsync(cancellationToken),
            clock.ToDhaka(order.PlacedAt),
            clock.DhakaNow,
            await MethodNameAsync(order.PaymentMethodCode, cancellationToken));

        var content = InvoiceRenderer.Render(model);

        OrderLog.InvoiceRendered(logger, order.OrderNumber, content.Length);

        return GeneralResponse<InvoiceFile>.Success(new InvoiceFile
        {
            Content = content,
            FileName = $"{order.OrderNumber}.pdf"
        });
    }

    private async Task<InvoiceShop> ShopAsync(CancellationToken cancellationToken)
    {
        var name = await settings.GetStringAsync(SettingKeys.StoreName, cancellationToken);

        return new InvoiceShop
        {
            // A shop that has not named itself still gets a header. The
            // alternative is an invoice that starts with a blank line.
            Name = string.IsNullOrWhiteSpace(name) ? FallbackShopName : name.Trim(),
            AddressLine = Clean(await settings.GetStringAsync(SettingKeys.StoreAddress, cancellationToken)),
            Phone = Clean(await settings.GetStringAsync(SettingKeys.StorePhone, cancellationToken)),
            Email = Clean(await settings.GetStringAsync(SettingKeys.StoreEmail, cancellationToken)),
            Bin = Clean(await settings.GetStringAsync(SettingKeys.StoreBin, cancellationToken))
        };
    }

    /// <summary>
    /// The readable name of the payment method, from the shop's own config.
    /// </summary>
    /// <remarks>
    /// English rather than the customer's language, matching the rest of the
    /// document. The fallbacks matter more than they look: a method row that has
    /// since been deleted must still produce a printable invoice for the orders
    /// placed through it, so an unknown code prints as itself rather than
    /// leaving the payment box empty.
    /// </remarks>
    private async Task<string> MethodNameAsync(string code, CancellationToken cancellationToken)
    {
        var config = await paymentMethods.GetByCodeAsync(code, cancellationToken);

        if (config is not null && !string.IsNullOrWhiteSpace(config.DisplayName.En))
        {
            return config.DisplayName.En;
        }

        return code switch
        {
            PaymentMethodCodes.CashOnDelivery => "Cash on delivery",
            PaymentMethodCodes.Bkash => "bKash",
            _ => code
        };
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
