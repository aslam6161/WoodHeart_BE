using WoodHeart.Domain.Entity.Payments;

namespace WoodHeart.Repository.Interfaces.Payments;

/// <summary>Reads the payment methods the shop has configured.</summary>
public interface IPaymentMethodConfigRepository : IRepository<PaymentMethodConfig>
{
    /// <summary>
    /// Every enabled method, in the shop's display order.
    /// </summary>
    /// <remarks>
    /// Ordered here rather than at the call site because the order is the
    /// shop's decision — which method it wants tried first — and a checkout
    /// page that re-sorted would quietly overrule it.
    /// </remarks>
    Task<IReadOnlyList<PaymentMethodConfig>> GetEnabledAsync(
        CancellationToken cancellationToken = default);

    Task<PaymentMethodConfig?> GetByCodeAsync(
        string code, CancellationToken cancellationToken = default);

    /// <summary>Everything, enabled or not, for the admin screen.</summary>
    Task<IReadOnlyList<PaymentMethodConfig>> GetAllOrderedAsync(
        CancellationToken cancellationToken = default);
}
