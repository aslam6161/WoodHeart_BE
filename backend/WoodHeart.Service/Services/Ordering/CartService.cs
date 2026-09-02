using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Pricing;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Mapping.Ordering;

namespace WoodHeart.Service.Services.Ordering;

/// <summary>
/// The basket.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cart is resolved from the caller, never from a client-supplied
/// id.</b> A signed-in customer's cart is found by their user id; a guest's by
/// the hash of the opaque token their browser holds. Nothing here accepts a
/// cart id from outside, so there is no request shape that reads a stranger's
/// basket — and once checkout puts a delivery address on it, that basket is
/// personal data.
/// </para>
/// <para>
/// <b>Prices are never taken from the request.</b> Every total is recomputed
/// from the variant's live price through <see cref="CartPricer"/>. A client
/// that could name a price is a client that can name zero.
/// </para>
/// </remarks>
public class CartService(
    ICartRepository carts,
    IProductVariantRepository variants,
    IPricingContextFactory pricing,
    ICurrentUserService currentUser,
    ITokenHasher hasher,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<CartService> logger) : ICartService
{
    public async Task<GeneralResponse<CartDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var cart = await FindAsync(cancellationToken);

        // No basket yet is the ordinary state of a first-time visitor, not a
        // 404. The header renders a zero and moves on.
        return cart is null
            ? GeneralResponse<CartDto>.Success(await EmptyAsync(cancellationToken))
            : GeneralResponse<CartDto>.Success(await ToDtoAsync(cart, cancellationToken));
    }

    public async Task<GeneralResponse<CartDto>> AddAsync(
        AddToCartDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Quantity is < CartRules.MinQuantity or > CartRules.MaxQuantityPerLine)
        {
            return Fail(OrderingErrors.QuantityInvalid,
                $"Quantity must be between {CartRules.MinQuantity} and {CartRules.MaxQuantityPerLine}.");
        }

        var variant = await variants.GetWithProductAsync(dto.VariantId, cancellationToken);

        if (variant is null)
        {
            return Fail(CatalogErrors.VariantNotFound, "That item no longer exists.");
        }

        // Checked here rather than trusted from the storefront, because the
        // storefront's copy of "is this on sale" is however old the page is. A
        // product withdrawn ten minutes ago must not be addable from a stale tab.
        if (!IsPurchasable(variant.Product, variant.IsActive))
        {
            return Fail(OrderingErrors.ProductNotPurchasable,
                "That item is not currently available to buy.");
        }

        var price = variant.EffectivePrice;

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var cart = await FindAsync(ct) ?? await CreateAsync(ct);

            if (cart is null)
            {
                return Fail(OrderingErrors.CartIdentityMissing,
                    "We could not start a basket for this session.");
            }

            if (cart.Status != CartStatus.Active)
            {
                return Fail(OrderingErrors.CartNotActive, "That basket has already been checked out.");
            }

            if (!string.Equals(cart.Currency, price.Currency, StringComparison.Ordinal))
            {
                return Fail(OrderingErrors.CurrencyMismatch,
                    "That item is priced in a different currency to your basket.");
            }

            var existing = cart.Lines.FirstOrDefault(l => l.ProductVariantId == dto.VariantId);

            if (existing is null)
            {
                cart.Lines.Add(new CartLine
                {
                    CartId = cart.Id,
                    ProductVariantId = dto.VariantId,
                    Quantity = dto.Quantity,
                    UnitPriceAtAdd = price
                });
            }
            else
            {
                // Adding the same thing again means "one more", not "start
                // again at one" — the second add of a dining chair is somebody
                // buying a pair.
                var combined = existing.Quantity + dto.Quantity;

                if (combined > CartRules.MaxQuantityPerLine)
                {
                    return Fail(OrderingErrors.QuantityTooLarge,
                        $"You can order at most {CartRules.MaxQuantityPerLine} of one item. "
                        + "For a larger order, please book a consultation.");
                }

                existing.Quantity = combined;
                existing.UnitPriceAtAdd = price;
            }

            Touch(cart);
            carts.Update(cart);
            await unitOfWork.SaveChangesAsync(ct);

            return GeneralResponse<CartDto>.Success(await ToDtoAsync(cart, ct));
        }, cancellationToken);
    }

    public async Task<GeneralResponse<CartDto>> UpdateLineAsync(
        long lineId, UpdateCartLineDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Quantity is < 0 or > CartRules.MaxQuantityPerLine)
        {
            return Fail(OrderingErrors.QuantityInvalid,
                $"Quantity must be between 0 and {CartRules.MaxQuantityPerLine}.");
        }

        var cart = await FindAsync(cancellationToken);

        if (cart is null)
        {
            return Fail(OrderingErrors.CartNotFound, "You do not have a basket yet.");
        }

        // Looked up inside the caller's own cart rather than by id across the
        // table. This is the check that stops a guessed line id from editing
        // somebody else's basket.
        var line = cart.Lines.FirstOrDefault(l => l.Id == lineId);

        if (line is null)
        {
            return Fail(OrderingErrors.CartLineNotFound, "That item is not in your basket.");
        }

        if (dto.Quantity == 0)
        {
            cart.Lines.Remove(line);
        }
        else
        {
            line.Quantity = dto.Quantity;
        }

        Touch(cart);
        carts.Update(cart);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<CartDto>.Success(await ToDtoAsync(cart, cancellationToken));
    }

    public Task<GeneralResponse<CartDto>> RemoveLineAsync(
        long lineId, CancellationToken cancellationToken = default) =>
        UpdateLineAsync(lineId, new UpdateCartLineDto { Quantity = 0 }, cancellationToken);

    public async Task<GeneralResponse<CartDto>> ClearAsync(CancellationToken cancellationToken = default)
    {
        var cart = await FindAsync(cancellationToken);

        if (cart is null)
        {
            return GeneralResponse<CartDto>.Success(await EmptyAsync(cancellationToken));
        }

        cart.Lines.Clear();

        Touch(cart);
        carts.Update(cart);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<CartDto>.Success(await ToDtoAsync(cart, cancellationToken));
    }

    public async Task<GeneralResponse<CartDto>> SetDeliveryZoneAsync(
        SetDeliveryZoneDto dto, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(dto.Zone))
        {
            return Fail(OrderingErrors.QuantityInvalid, "That is not a delivery zone we serve.");
        }

        var cart = await FindAsync(cancellationToken);

        if (cart is null)
        {
            return Fail(OrderingErrors.CartNotFound, "You do not have a basket yet.");
        }

        cart.DeliveryZone = dto.Zone;

        Touch(cart);
        carts.Update(cart);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<CartDto>.Success(await ToDtoAsync(cart, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<GeneralResponse> MergeGuestCartAsync(
        string anonymousId, long customerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(anonymousId))
        {
            return GeneralResponse.Success();
        }

        var guestCart = await carts.GetActiveForGuestAsync(hasher.Hash(anonymousId), cancellationToken);

        if (guestCart is null || guestCart.Lines.Count == 0)
        {
            return GeneralResponse.Success();
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var memberCart = await carts.GetActiveForCustomerAsync(customerId, ct);

            if (memberCart is null)
            {
                // Nothing to merge into: hand the whole basket over rather than
                // copying it line by line. Fewer writes, and the cart keeps its
                // id, so anything already referring to it still resolves.
                guestCart.CustomerId = customerId;
                guestCart.AnonymousToken = null;

                Touch(guestCart);
                carts.Update(guestCart);
                await unitOfWork.SaveChangesAsync(ct);

                CartLog.GuestCartAdopted(logger, guestCart.Id, customerId, guestCart.Lines.Count);

                return GeneralResponse.Success();
            }

            foreach (var guestLine in guestCart.Lines)
            {
                var existing = memberCart.Lines
                    .FirstOrDefault(l => l.ProductVariantId == guestLine.ProductVariantId);

                if (existing is null)
                {
                    memberCart.Lines.Add(new CartLine
                    {
                        CartId = memberCart.Id,
                        ProductVariantId = guestLine.ProductVariantId,
                        Quantity = guestLine.Quantity,
                        UnitPriceAtAdd = guestLine.UnitPriceAtAdd
                    });
                }
                else
                {
                    // Summed, then capped. Two devices each holding 60 of
                    // something must not produce a line of 120 that no later
                    // validation would accept.
                    existing.Quantity =
                        Math.Min(existing.Quantity + guestLine.Quantity, CartRules.MaxQuantityPerLine);
                }
            }

            // The guest cart is emptied and retired rather than deleted: it is
            // the record of a session, and Phase 5's abandoned-cart work reads
            // these.
            guestCart.Lines.Clear();
            guestCart.Status = CartStatus.CheckedOut;
            guestCart.AnonymousToken = null;

            Touch(memberCart);
            carts.Update(memberCart);
            carts.Update(guestCart);

            await unitOfWork.SaveChangesAsync(ct);

            CartLog.GuestCartMerged(logger, guestCart.Id, memberCart.Id, memberCart.Lines.Count);

            return GeneralResponse.Success();
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Resolution
    // -------------------------------------------------------------------------

    /// <summary>The caller's active cart, by user id or by guest token hash.</summary>
    private async Task<Cart?> FindAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is { } userId)
        {
            return await carts.GetActiveForCustomerAsync(userId, cancellationToken);
        }

        var anonymousId = currentUser.AnonymousId;

        return string.IsNullOrWhiteSpace(anonymousId)
            ? null
            : await carts.GetActiveForGuestAsync(hasher.Hash(anonymousId), cancellationToken);
    }

    /// <summary>
    /// Starts a cart for whoever is asking, or returns null when a guest has no
    /// anonymous id at all.
    /// </summary>
    /// <remarks>
    /// Without an id there is nothing to attach a basket to, and creating one
    /// anyway would write a fresh orphan row on every request — which is how a
    /// carts table ends up with a million rows and one real customer.
    /// </remarks>
    private async Task<Cart?> CreateAsync(CancellationToken cancellationToken)
    {
        var anonymousId = currentUser.AnonymousId;

        if (currentUser.UserId is null && string.IsNullOrWhiteSpace(anonymousId))
        {
            return null;
        }

        var cart = new Cart
        {
            CustomerId = currentUser.UserId,
            AnonymousToken = currentUser.UserId is null ? hasher.Hash(anonymousId!) : null,
            Currency = GlobalConstants.Currency,
            Status = CartStatus.Active,
            ExpiresAt = clock.UtcNow.AddDays(CartRules.LifetimeDays)
        };

        await carts.InsertAsync(cart, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return cart;
    }

    /// <summary>Pushes the expiry out. Any change means the basket is still live.</summary>
    private void Touch(Cart cart) =>
        cart.ExpiresAt = clock.UtcNow.AddDays(CartRules.LifetimeDays);

    // -------------------------------------------------------------------------
    // Pricing and mapping
    // -------------------------------------------------------------------------

    private async Task<CartDto> ToDtoAsync(Cart cart, CancellationToken cancellationToken)
    {
        var context = await pricing.BuildAsync(
            cart.DeliveryZone, cart.DeliveryFeeOverride, cancellationToken);

        // Unavailable lines are shown but not charged for. Billing someone for
        // a product that has been withdrawn is worse than either dropping it
        // silently or refusing the whole basket.
        var priceable = cart.Lines
            .Where(line => IsPurchasable(line.ProductVariant.Product, line.ProductVariant.IsActive))
            .Select(line => new PricedLine(
                line.Quantity,
                line.ProductVariant.EffectivePrice,
                line.ProductVariant.Product.DeliveryChargeInsideDhaka,
                line.ProductVariant.Product.DeliveryChargeOutsideDhaka))
            .ToList();

        var totals = CartPricer.Price(priceable, context);

        return CartMapper.ToDto(cart, totals, context);
    }

    private async Task<CartDto> EmptyAsync(CancellationToken cancellationToken)
    {
        var context = await pricing.BuildAsync(null, null, cancellationToken);

        return CartMapper.Empty(CartPricer.Price([], context), context);
    }

    private static bool IsPurchasable(Domain.Entity.Catalog.Product product, bool variantActive) =>
        variantActive && product is { Status: ProductStatus.Active, IsDeleted: false };

    private static GeneralResponse<CartDto> Fail(string code, string message) =>
        GeneralResponse<CartDto>.Fail(code, message);
}
