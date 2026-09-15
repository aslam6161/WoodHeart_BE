using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Interfaces.Ordering;

/// <summary>Draws the invoice for a placed order.</summary>
/// <remarks>
/// Its own service rather than a method on <c>IAdminOrderService</c>: nothing
/// here changes an order, and the only thing it has in common with that service
/// is the table it reads from. Keeping them apart also means the day a customer
/// can download their own invoice, the endpoint reuses this without dragging
/// the staff-only operations along with it.
/// </remarks>
public interface IInvoiceService
{
    /// <summary>The invoice for an order, as a PDF file ready to be returned.</summary>
    Task<GeneralResponse<InvoiceFile>> RenderAsync(
        string orderNumber, CancellationToken cancellationToken = default);
}
