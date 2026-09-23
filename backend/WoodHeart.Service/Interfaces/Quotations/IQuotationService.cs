using WoodHeart.Domain.Enums.Quotations;
using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.DTOs.Quotations;

namespace WoodHeart.Service.Interfaces.Quotations;

/// <summary>
/// What the shop offered to do, and what becomes of the offer.
/// </summary>
/// <remarks>
/// The funnel Phase 4 exists for: a consultation is an hour sold at cost, and
/// the money is in what the designer proposes afterwards. Everything here
/// serves that one sentence.
/// </remarks>
public interface IQuotationService
{
    // --- The shop's side --------------------------------------------------------

    Task<GeneralResponse<PagedResult<QuotationListItemDto>>> SearchAsync(
        QuotationQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>One quotation for staff: internal notes, and the moves it can make.</summary>
    Task<GeneralResponse<QuotationDto>> GetForStaffAsync(
        string quotationNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a quotation, lines and all.
    /// </summary>
    /// <remarks>
    /// Saved whole rather than line by line. A quotation is one document — the
    /// discount only means something beside the lines it comes off — and a
    /// partial save is a quotation whose total disagrees with itself. Only a
    /// draft can be written to; changing a sent one means pulling it back
    /// first, which is deliberate and leaves a trail.
    /// </remarks>
    Task<GeneralResponse<QuotationDto>> SaveAsync(
        long? id, SaveQuotationDto dto, CancellationToken cancellationToken = default);

    /// <summary>Confirm, withdraw, expire — whatever the machine allows from here.</summary>
    Task<GeneralResponse<QuotationDto>> SetStatusAsync(
        string quotationNumber,
        QuotationStatus status,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns an accepted quotation into an order.
    /// </summary>
    /// <remarks>
    /// <b>It does not re-price.</b> A quotation is a promise of a figure, and
    /// honouring it is the whole point of having given it — so the order takes
    /// the quoted money as it stands. What is added is whatever the chosen
    /// payment method charges, which is a cost of paying rather than of buying,
    /// and which the quotation says in as many words will be.
    /// </remarks>
    Task<GeneralResponse<PlacedOrderDto>> ConvertAsync(
        string quotationNumber,
        ConvertQuotationDto dto,
        CancellationToken cancellationToken = default);

    // --- The customer's side -----------------------------------------------------

    /// <summary>One quotation for whoever it is for — by number plus phone for a guest.</summary>
    Task<GeneralResponse<QuotationDto>> GetAsync(
        string quotationNumber, string? contactPhone, CancellationToken cancellationToken = default);

    /// <summary>The customer saying yes.</summary>
    Task<GeneralResponse<QuotationDto>> AcceptAsync(
        string quotationNumber, string? contactPhone, CancellationToken cancellationToken = default);

    /// <summary>The customer saying no, and ideally why.</summary>
    Task<GeneralResponse<QuotationDto>> DeclineAsync(
        string quotationNumber,
        string? contactPhone,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<GeneralResponse<PagedResult<QuotationDto>>> GetMineAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);
}
