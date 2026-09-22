using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Promotions;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.DTOs.Promotions;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Promotions;
using WoodHeart.Service.Mapping.Promotions;

namespace WoodHeart.Service.Services.Promotions;

/// <summary>
/// Writing discounts, and reading what they have cost.
/// </summary>
/// <remarks>
/// <para>
/// <b>A discount that has been used is only half editable.</b> Its name, its
/// window, its limits and its status can all be changed; its type, its value
/// and what it applies to cannot. Orders point at it and say what it gave them,
/// and turning 20% into 50% after the fact would leave last month's report
/// disagreeing with last month's invoices. Pause it and write a new one — which
/// is what a shop would do on paper anyway.
/// </para>
/// <para>
/// Nothing here decides what a discount is worth. That is
/// <c>DiscountEngine</c>'s, and keeping the two apart is what makes the engine
/// exhaustively testable.
/// </para>
/// </remarks>
public class DiscountAdminService(
    IDiscountRepository discounts,
    IPromotionUsageRepository usages,
    ICategoryRepository categories,
    IProductRepository products,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<DiscountAdminService> logger) : IDiscountAdminService
{
    public async Task<GeneralResponse<PagedResult<DiscountListItemDto>>> SearchAsync(
        DiscountQueryDto query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await discounts.SearchAsync(
            query.Term, query.Status, query.Page, query.PageSize, cancellationToken);

        // One grouped query for the whole page rather than one per row: a list
        // of twenty discounts must not be twenty-one round trips.
        var counts = await usages.GetUsageAsync(
            [.. page.Select(discount => discount.Id)], null, null, cancellationToken);

        var now = clock.UtcNow;

        return GeneralResponse<PagedResult<DiscountListItemDto>>.Success(new PagedResult<DiscountListItemDto>
        {
            Items =
            [
                .. page.Select(discount => PromotionMapper.ToListItem(
                    discount,
                    now,
                    counts.TryGetValue(discount.Id, out var used) ? used.Total : 0))
            ],
            Total = page.TotalCount,
            Page = page.CurrentPage,
            PageSize = page.PageSize
        });
    }

    public async Task<GeneralResponse<DiscountDto>> GetAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var discount = await discounts.GetWithTargetsAsync(id, cancellationToken);

        if (discount is null)
        {
            return Fail(PromotionErrors.DiscountNotFound, "That discount no longer exists.");
        }

        return GeneralResponse<DiscountDto>.Success(await ToDtoAsync(discount, cancellationToken));
    }

    public async Task<GeneralResponse<DiscountDto>> CreateAsync(
        SaveDiscountDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var invalid = await ValidateAsync(dto, excludeId: null, ct);

            if (invalid is not null)
            {
                return invalid;
            }

            var discount = new Discount();

            Apply(dto, discount);
            ApplyTargets(dto, discount);

            await discounts.InsertAsync(discount, ct);
            await unitOfWork.SaveChangesAsync(ct);

            PromotionLog.StatusChanged(logger, discount.Name, discount.Status, Actor());

            return GeneralResponse<DiscountDto>.Success(
                await ToDtoAsync(discount, ct), id: discount.Id);
        }, cancellationToken);
    }

    public async Task<GeneralResponse<DiscountDto>> UpdateAsync(
        long id, SaveDiscountDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var discount = await discounts.GetWithTargetsAsync(id, ct);

            if (discount is null)
            {
                return Fail(PromotionErrors.DiscountNotFound, "That discount no longer exists.");
            }

            var invalid = await ValidateAsync(dto, excludeId: id, ct);

            if (invalid is not null)
            {
                return invalid;
            }

            var (orders, _) = await usages.TotalsAsync(id, ct);

            if (orders > 0 && ChangesTheDeal(dto, discount))
            {
                return Fail(
                    PromotionErrors.DiscountInUse,
                    $"This discount has already been given to {orders} order(s), so what it is worth "
                    + "and what it applies to can no longer be changed. Pause it and create a new one.");
            }

            Apply(dto, discount);

            if (orders == 0)
            {
                ApplyTargets(dto, discount);
            }

            discounts.Update(discount);
            await unitOfWork.SaveChangesAsync(ct);

            return GeneralResponse<DiscountDto>.Success(await ToDtoAsync(discount, ct));
        }, cancellationToken);
    }

    public async Task<GeneralResponse<DiscountDto>> SetStatusAsync(
        long id, DiscountStatus status, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status))
        {
            return Fail(PromotionErrors.ValueInvalid, "That is not a status a discount can be in.");
        }

        var discount = await discounts.GetWithTargetsAsync(id, cancellationToken);

        if (discount is null)
        {
            return Fail(PromotionErrors.DiscountNotFound, "That discount no longer exists.");
        }

        discount.Status = status;

        discounts.Update(discount);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        PromotionLog.StatusChanged(logger, discount.Name, status, Actor());

        return GeneralResponse<DiscountDto>.Success(await ToDtoAsync(discount, cancellationToken));
    }

    public async Task<GeneralResponse<PagedResult<PromotionUsageDto>>> GetUsageAsync(
        long id, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!await discounts.AnyAsync(x => x.Id == id, cancellationToken))
        {
            return GeneralResponse<PagedResult<PromotionUsageDto>>.Fail(
                PromotionErrors.DiscountNotFound, "That discount no longer exists.");
        }

        var rows = await usages.SearchAsync(
            id,
            Math.Max(page, 1),
            Math.Clamp(pageSize, 1, OrderRules.MaxPageSize),
            cancellationToken);

        return GeneralResponse<PagedResult<PromotionUsageDto>>.Success(new PagedResult<PromotionUsageDto>
        {
            Items = [.. rows.Select(PromotionMapper.ToUsageDto)],
            Total = rows.TotalCount,
            Page = rows.CurrentPage,
            PageSize = rows.PageSize
        });
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    /// <summary>The first thing wrong with this discount, or null.</summary>
    private async Task<GeneralResponse<DiscountDto>?> ValidateAsync(
        SaveDiscountDto dto, long? excludeId, CancellationToken cancellationToken)
    {
        var code = Discount.NormaliseCode(dto.Code);

        if (code is not null && await discounts.CodeExistsAsync(code, excludeId, cancellationToken))
        {
            return Fail(PromotionErrors.CodeTaken, $"Another discount already uses the code {code}.");
        }

        switch (dto.Type)
        {
            case DiscountType.Percentage when dto.Value is <= 0m or > 100m:
                return Fail(PromotionErrors.ValueInvalid, "A percentage discount must be between 0 and 100.");

            case DiscountType.FixedAmount when dto.Value <= 0m:
                return Fail(PromotionErrors.ValueInvalid, "An amount off must be more than zero.");
        }

        if (dto.StartsAt is { } starts && dto.EndsAt is { } ends && ends <= starts)
        {
            return Fail(PromotionErrors.WindowInvalid, "The end of the window must be after its start.");
        }

        foreach (var target in dto.Targets)
        {
            if (target.CategoryId is null == target.ProductId is null)
            {
                return Fail(
                    PromotionErrors.TargetInvalid,
                    "Each restriction names either a category or a product, never both and never neither.");
            }

            if (target.CategoryId is { } categoryId
                && !await categories.AnyAsync(x => x.Id == categoryId, cancellationToken))
            {
                return Fail(PromotionErrors.TargetNotFound, "One of the categories no longer exists.");
            }

            if (target.ProductId is { } productId
                && !await products.AnyAsync(x => x.Id == productId, cancellationToken))
            {
                return Fail(PromotionErrors.TargetNotFound, "One of the products no longer exists.");
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this edit changes what the discount is worth or what it applies
    /// to — the parts an order that already used it has a claim on.
    /// </summary>
    private static bool ChangesTheDeal(SaveDiscountDto dto, Discount discount) =>
        dto.Type != discount.Type
        || dto.Value != discount.Value
        || dto.MaxDiscountAmount != discount.MaxDiscountAmount?.Amount
        || !SameTargets(dto, discount);

    private static bool SameTargets(SaveDiscountDto dto, Discount discount) =>
        dto.Targets.Count == discount.Targets.Count
        && dto.Targets.All(target => discount.Targets.Any(existing =>
            existing.CategoryId == target.CategoryId && existing.ProductId == target.ProductId));

    // -------------------------------------------------------------------------
    // Writing
    // -------------------------------------------------------------------------

    private static void Apply(SaveDiscountDto dto, Discount discount)
    {
        discount.Name = dto.Name.Trim();
        discount.Code = Discount.NormaliseCode(dto.Code);
        discount.Type = dto.Type;
        discount.Value = dto.Type == DiscountType.FreeShipping ? 0m : dto.Value;
        discount.MaxDiscountAmount = dto.MaxDiscountAmount is { } max ? Money.Taka(max) : null;
        discount.MinSubtotal = dto.MinSubtotal is { } minimum ? Money.Taka(minimum) : null;
        discount.MinQuantity = dto.MinQuantity;
        discount.FirstOrderOnly = dto.FirstOrderOnly;
        discount.DeliveryZones = [.. dto.DeliveryZones.Distinct()];
        discount.PaymentMethods = [.. dto.PaymentMethods
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .Select(method => method.Trim().ToLowerInvariant())
            .Distinct()];
        discount.StartsAt = dto.StartsAt;
        discount.EndsAt = dto.EndsAt;
        discount.UsageLimitTotal = dto.UsageLimitTotal;
        discount.UsageLimitPerCustomer = dto.UsageLimitPerCustomer;
        discount.Stackable = dto.Stackable;
        discount.Priority = dto.Priority;
        discount.Status = dto.Status;
    }

    /// <summary>
    /// Replaces the targets wholesale.
    /// </summary>
    /// <remarks>
    /// The form sends the complete set, the same way the settings screen does.
    /// A partial update of a restriction list is the shape that quietly leaves
    /// a category behind, and a discount applying to something it was edited
    /// out of is exactly the failure this whole module has to avoid.
    /// </remarks>
    private static void ApplyTargets(SaveDiscountDto dto, Discount discount)
    {
        discount.Targets.Clear();

        foreach (var target in dto.Targets)
        {
            discount.Targets.Add(new DiscountTarget
            {
                CategoryId = target.CategoryId,
                ProductId = target.ProductId
            });
        }
    }

    private async Task<DiscountDto> ToDtoAsync(Discount discount, CancellationToken cancellationToken)
    {
        var (orders, total) = await usages.TotalsAsync(discount.Id, cancellationToken);

        await FillTargetNamesAsync(discount, cancellationToken);

        return PromotionMapper.ToDto(discount, clock.UtcNow, orders, total);
    }

    /// <summary>
    /// Loads the names behind freshly written targets, so a create returns the
    /// same shape a read does.
    /// </summary>
    /// <remarks>
    /// A target built from a DTO has an id and no navigation. Without this the
    /// admin screen would show a chip with a name after a refresh and a blank
    /// one immediately after saving — the kind of inconsistency that makes
    /// people press refresh to check their own work.
    /// </remarks>
    private async Task FillTargetNamesAsync(Discount discount, CancellationToken cancellationToken)
    {
        foreach (var target in discount.Targets)
        {
            if (target is { CategoryId: { } categoryId, Category: null })
            {
                target.Category = await categories.GetByIdAsync(categoryId, cancellationToken);
            }

            if (target is { ProductId: { } productId, Product: null })
            {
                target.Product = await products.GetByIdAsync(productId, cancellationToken);
            }
        }
    }

    private string Actor() => currentUser.PhoneNumber ?? "System";

    private static GeneralResponse<DiscountDto> Fail(string code, string message) =>
        GeneralResponse<DiscountDto>.Fail(code, message);
}
