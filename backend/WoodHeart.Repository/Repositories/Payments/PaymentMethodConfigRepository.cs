using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Repository.Interfaces.Payments;

namespace WoodHeart.Repository.Repositories.Payments;

public class PaymentMethodConfigRepository(DataContext context)
    : Repository<PaymentMethodConfig>(context), IPaymentMethodConfigRepository
{
    public async Task<IReadOnlyList<PaymentMethodConfig>> GetEnabledAsync(
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// Tracked, because the admin screen that reads one method by code is about
    /// to change it.
    /// </remarks>
    public async Task<PaymentMethodConfig?> GetByCodeAsync(
        string code, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

    public async Task<IReadOnlyList<PaymentMethodConfig>> GetAllOrderedAsync(
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);
}
