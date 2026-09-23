using WoodHeart.Domain.Entity.Quotations;
using WoodHeart.Domain.Enums.Quotations;

namespace WoodHeart.Repository.Interfaces.Quotations;

/// <summary>What the shop offered. Stages changes; does not commit them.</summary>
public interface IQuotationRepository : IRepository<Quotation>
{
    /// <summary>One quotation with everything a screen renders.</summary>
    Task<Quotation?> GetByNumberAsync(
        string quotationNumber, CancellationToken cancellationToken = default);

    Task<Quotation?> GetDetailAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The shop's list, filtered as the board asked.</summary>
    Task<PagedList<Quotation>> SearchAsync(
        QuotationSearch criteria, CancellationToken cancellationToken = default);

    /// <summary>A signed-in customer's own, newest first.</summary>
    Task<IReadOnlyList<Quotation>> GetForCustomerAsync(
        long customerId, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountForCustomerAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The quotations that have run out of time and do not yet say so.
    /// </summary>
    /// <remarks>
    /// <c>QuotationStatusMachine.HasLapsed</c> is true the moment the date
    /// passes, whatever the column says; this is for the job that makes it
    /// durable, so "how many went cold" can be counted rather than recomputed
    /// against a moving today.
    /// </remarks>
    Task<IReadOnlyList<Quotation>> GetLapsedAsync(
        DateOnly todayInDhaka, int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// What the board asked the quotations for.
/// </summary>
/// <remarks>
/// A record rather than six parameters, and declared here rather than taken as
/// a DTO, because Repository cannot see Service.
/// </remarks>
/// <param name="Term">Number, customer name or phone. Null matches all.</param>
public readonly record struct QuotationSearch(
    string? Term,
    QuotationStatus? Status,
    long? BookingId,
    int Page,
    int PageSize);
