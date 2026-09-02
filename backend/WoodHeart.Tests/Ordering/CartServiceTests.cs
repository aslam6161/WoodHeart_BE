using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Services.Ordering;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// The basket's behaviour, as distinct from its arithmetic.
/// </summary>
/// <remarks>
/// The two things worth the most attention here are <b>identity</b> — a cart is
/// resolved from the caller, so no request shape reaches somebody else's — and
/// <b>the merge on sign-in</b>, which is the operation that can silently lose a
/// customer's items.
/// </remarks>
public class CartServiceTests
{
    private const long CustomerId = 42;
    private const string AnonymousId = "guest-token";
    private const string AnonymousHash = "hashed:guest-token";

    private readonly ICartRepository _carts = Substitute.For<ICartRepository>();
    private readonly IProductVariantRepository _variants = Substitute.For<IProductVariantRepository>();
    private readonly IPricingContextFactory _pricing = Substitute.For<IPricingContextFactory>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly ITokenHasher _hasher = Substitute.For<ITokenHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();

    public CartServiceTests()
    {
        _hasher.Hash(Arg.Any<string>()).Returns(call => $"hashed:{call.Arg<string>()}");

        _pricing.BuildAsync(Arg.Any<DeliveryZone?>(), Arg.Any<CancellationToken>())
            .Returns(new PricingContext(0m, PricesIncludeVat: true));

        // The real unit of work runs the delegate inside a transaction. The
        // substitute just runs it, so the tests exercise the body rather than
        // EF's transaction handling.
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<CartDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse<CartDto>>>>()(CancellationToken.None));

        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse>>>()(CancellationToken.None));
    }

    private CartService CreateService() =>
        new(_carts,
            _variants,
            _pricing,
            _currentUser,
            _hasher,
            _clock,
            _unitOfWork,
            NullLogger<CartService>.Instance);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_signed_in_customer_gets_their_own_cart()
    {
        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(CartWith(Variant()));

        var result = await CreateService().GetAsync();

        result.IsSuccess.ShouldBeTrue();
        await _carts.Received(1).GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>());

        // Never by guest token: a signed-in customer with a stale anonymous
        // cookie must not be handed the basket that cookie names.
        await _carts.DidNotReceive().GetActiveForGuestAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_guests_cart_is_found_by_the_hash_of_their_token_never_the_token()
    {
        _currentUser.UserId.Returns((long?)null);
        _currentUser.AnonymousId.Returns(AnonymousId);

        await CreateService().GetAsync();

        // The raw token must not reach the query. It is a bearer credential for
        // a basket, and a hash is what a leaked backup should expose.
        await _carts.Received(1).GetActiveForGuestAsync(AnonymousHash, Arg.Any<CancellationToken>());
        await _carts.DidNotReceive().GetActiveForGuestAsync(AnonymousId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_basket_yet_returns_an_empty_one_rather_than_an_error()
    {
        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns((Cart?)null);

        var result = await CreateService().GetAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Data!.Lines.ShouldBeEmpty();
        result.Data.Totals.GrandTotal.ShouldBe(0m);
    }

    [Fact]
    public async Task A_guest_with_no_anonymous_id_cannot_start_a_basket()
    {
        // Without an id there is nothing to attach a cart to, and creating one
        // anyway writes a fresh orphan row on every request.
        _currentUser.UserId.Returns((long?)null);
        _currentUser.AnonymousId.Returns((string?)null);
        _variants.GetWithProductAsync(1, Arg.Any<CancellationToken>()).Returns(Variant());

        var result = await CreateService().AddAsync(new AddToCartDto { VariantId = 1, Quantity = 1 });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.CartIdentityMissing);
        await _carts.DidNotReceive().InsertAsync(Arg.Any<Cart>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Adding
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Adding_the_same_variant_twice_increments_rather_than_duplicating()
    {
        var variant = Variant();
        var cart = CartWith(variant, quantity: 1);

        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(cart);
        _variants.GetWithProductAsync(variant.Id, Arg.Any<CancellationToken>()).Returns(variant);

        var result = await CreateService()
            .AddAsync(new AddToCartDto { VariantId = variant.Id, Quantity = 2 });

        result.IsSuccess.ShouldBeTrue();
        cart.Lines.Count.ShouldBe(1);
        cart.Lines.Single().Quantity.ShouldBe(3);
    }

    [Fact]
    public async Task Adding_beyond_the_per_line_cap_is_refused()
    {
        var variant = Variant();
        var cart = CartWith(variant, quantity: CartRules.MaxQuantityPerLine - 1);

        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(cart);
        _variants.GetWithProductAsync(variant.Id, Arg.Any<CancellationToken>()).Returns(variant);

        var result = await CreateService()
            .AddAsync(new AddToCartDto { VariantId = variant.Id, Quantity = 5 });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.QuantityTooLarge);

        // Refused outright rather than clamped: silently giving somebody 99 of
        // something when they asked for 103 is a worse surprise than a message.
        cart.Lines.Single().Quantity.ShouldBe(CartRules.MaxQuantityPerLine - 1);
    }

    [Fact]
    public async Task A_withdrawn_product_cannot_be_added_from_a_stale_page()
    {
        // The storefront's copy of "is this on sale" is however old the tab is.
        // The check has to happen here, not there.
        var variant = Variant();
        variant.Product.Status = ProductStatus.Archived;

        _currentUser.UserId.Returns(CustomerId);
        _variants.GetWithProductAsync(variant.Id, Arg.Any<CancellationToken>()).Returns(variant);

        var result = await CreateService()
            .AddAsync(new AddToCartDto { VariantId = variant.Id, Quantity = 1 });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.ProductNotPurchasable);
    }

    [Fact]
    public async Task A_deactivated_variant_cannot_be_added_even_when_its_product_is_live()
    {
        var variant = Variant();
        variant.IsActive = false;

        _currentUser.UserId.Returns(CustomerId);
        _variants.GetWithProductAsync(variant.Id, Arg.Any<CancellationToken>()).Returns(variant);

        var result = await CreateService()
            .AddAsync(new AddToCartDto { VariantId = variant.Id, Quantity = 1 });

        result.ErrorCode.ShouldBe(OrderingErrors.ProductNotPurchasable);
    }

    [Fact]
    public async Task An_unknown_variant_is_a_not_found_rather_than_a_crash()
    {
        _currentUser.UserId.Returns(CustomerId);
        _variants.GetWithProductAsync(999, Arg.Any<CancellationToken>()).Returns((ProductVariant?)null);

        var result = await CreateService().AddAsync(new AddToCartDto { VariantId = 999, Quantity = 1 });

        result.ErrorCode.ShouldBe(CatalogErrors.VariantNotFound);
    }

    // -------------------------------------------------------------------------
    // Editing lines
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_line_id_from_someone_elses_basket_is_not_found()
    {
        // The check that matters: line ids are small integers anyone can change
        // in a request. Looking the line up inside the resolved cart, rather
        // than by id across the table, is what makes guessing useless.
        var cart = CartWith(Variant(), lineId: 7);

        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(cart);

        var result = await CreateService()
            .UpdateLineAsync(8, new UpdateCartLineDto { Quantity = 1 });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.CartLineNotFound);
        cart.Lines.Single().Quantity.ShouldBe(1);
    }

    [Fact]
    public async Task Setting_a_quantity_to_zero_removes_the_line()
    {
        var cart = CartWith(Variant(), lineId: 7);

        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(cart);

        var result = await CreateService()
            .UpdateLineAsync(7, new UpdateCartLineDto { Quantity = 0 });

        result.IsSuccess.ShouldBeTrue();
        cart.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Clearing_empties_the_basket_without_discarding_the_cart()
    {
        var cart = CartWith(Variant());

        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(cart);

        await CreateService().ClearAsync();

        cart.Lines.ShouldBeEmpty();
        cart.Status.ShouldBe(CartStatus.Active);
    }

    [Fact]
    public async Task Choosing_a_zone_is_what_makes_delivery_priceable()
    {
        var cart = CartWith(Variant());

        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(cart);

        var result = await CreateService()
            .SetDeliveryZoneAsync(new SetDeliveryZoneDto { Zone = DeliveryZone.OutsideDhaka });

        result.IsSuccess.ShouldBeTrue();
        cart.DeliveryZone.ShouldBe(DeliveryZone.OutsideDhaka);
        await _pricing.Received().BuildAsync(DeliveryZone.OutsideDhaka, Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // What the cart reports
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_price_that_moved_since_it_was_added_is_flagged_not_hidden()
    {
        var variant = Variant(price: 45_000m);
        var cart = CartWith(variant);
        cart.Lines.Single().UnitPriceAtAdd = Money.Taka(42_000m);

        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(cart);

        var result = await CreateService().GetAsync();

        result.Data!.HasPriceChanges.ShouldBeTrue();

        var line = result.Data.Lines.Single();
        line.PriceChanged.ShouldBeTrue();
        line.UnitPrice.ShouldBe(45_000m);
        line.UnitPriceAtAdd.ShouldBe(42_000m);

        // The live price is what is charged. A stored price a customer can hold
        // indefinitely by leaving a tab open is not a price, it is a coupon.
        line.LineTotal.ShouldBe(45_000m);
    }

    [Fact]
    public async Task A_withdrawn_line_is_shown_but_not_charged_for()
    {
        var live = Variant(id: 1, price: 1000m);
        var withdrawn = Variant(id: 2, price: 5000m);
        withdrawn.Product.Status = ProductStatus.Archived;

        var cart = CartWith(live);
        cart.Lines.Add(new CartLine
        {
            Id = 2,
            CartId = cart.Id,
            ProductVariantId = withdrawn.Id,
            ProductVariant = withdrawn,
            Quantity = 1,
            UnitPriceAtAdd = Money.Taka(5000m)
        });

        _currentUser.UserId.Returns(CustomerId);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(cart);

        var result = await CreateService().GetAsync();

        // Both lines visible — silently dropping something a customer chose is
        // worse than showing it greyed out with a reason.
        result.Data!.Lines.Count.ShouldBe(2);
        result.Data.HasUnavailableLines.ShouldBeTrue();

        // ...but only the live one is in the total.
        result.Data.Totals.Subtotal.ShouldBe(1000m);
    }

    // -------------------------------------------------------------------------
    // The merge on sign-in
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Signing_in_with_no_member_cart_hands_the_guest_cart_over_whole()
    {
        var guest = CartWith(Variant());
        guest.CustomerId = null;
        guest.AnonymousToken = AnonymousHash;

        _carts.GetActiveForGuestAsync(AnonymousHash, Arg.Any<CancellationToken>()).Returns(guest);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns((Cart?)null);

        var result = await CreateService().MergeGuestCartAsync(AnonymousId, CustomerId);

        result.IsSuccess.ShouldBeTrue();
        guest.CustomerId.ShouldBe(CustomerId);

        // The token is cleared, or the cart stays reachable by whoever still
        // holds that cookie — including on a shared computer.
        guest.AnonymousToken.ShouldBeNull();
        guest.Lines.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Merging_sums_quantities_rather_than_replacing_them()
    {
        // Somebody put a lamp in a basket on their phone, signed in on a
        // laptop, and added the same lamp: they meant to have two. A merge that
        // discarded one is a shop losing a sale it had already made.
        var variant = Variant();

        var guest = CartWith(variant, quantity: 1);
        guest.CustomerId = null;
        guest.AnonymousToken = AnonymousHash;

        var member = CartWith(variant, quantity: 1, cartId: 2, lineId: 9);

        _carts.GetActiveForGuestAsync(AnonymousHash, Arg.Any<CancellationToken>()).Returns(guest);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(member);

        await CreateService().MergeGuestCartAsync(AnonymousId, CustomerId);

        member.Lines.Count.ShouldBe(1);
        member.Lines.Single().Quantity.ShouldBe(2);
    }

    [Fact]
    public async Task Merging_caps_the_combined_quantity()
    {
        // Two devices each holding 60 must not produce a line of 120 that no
        // later validation would accept.
        var variant = Variant();

        var guest = CartWith(variant, quantity: 60);
        guest.CustomerId = null;
        guest.AnonymousToken = AnonymousHash;

        var member = CartWith(variant, quantity: 60, cartId: 2, lineId: 9);

        _carts.GetActiveForGuestAsync(AnonymousHash, Arg.Any<CancellationToken>()).Returns(guest);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(member);

        await CreateService().MergeGuestCartAsync(AnonymousId, CustomerId);

        member.Lines.Single().Quantity.ShouldBe(CartRules.MaxQuantityPerLine);
    }

    [Fact]
    public async Task Merging_moves_a_variant_the_member_cart_does_not_have()
    {
        var guestVariant = Variant(id: 1);
        var memberVariant = Variant(id: 2);

        var guest = CartWith(guestVariant, quantity: 3);
        guest.CustomerId = null;
        guest.AnonymousToken = AnonymousHash;

        var member = CartWith(memberVariant, quantity: 1, cartId: 2, lineId: 9);

        _carts.GetActiveForGuestAsync(AnonymousHash, Arg.Any<CancellationToken>()).Returns(guest);
        _carts.GetActiveForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(member);

        await CreateService().MergeGuestCartAsync(AnonymousId, CustomerId);

        member.Lines.Count.ShouldBe(2);
        member.Lines.ShouldContain(l => l.ProductVariantId == 1 && l.Quantity == 3);

        // The guest cart is retired rather than deleted: it is the record of a
        // session, and abandoned-cart recovery reads these.
        guest.Lines.ShouldBeEmpty();
        guest.Status.ShouldBe(CartStatus.CheckedOut);
    }

    [Fact]
    public async Task Merging_with_no_guest_cart_is_a_no_op_not_a_failure()
    {
        _carts.GetActiveForGuestAsync(AnonymousHash, Arg.Any<CancellationToken>()).Returns((Cart?)null);

        var result = await CreateService().MergeGuestCartAsync(AnonymousId, CustomerId);

        result.IsSuccess.ShouldBeTrue();
        await _carts.DidNotReceive().GetActiveForCustomerAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static ProductVariant Variant(long id = 1, decimal price = 1000m) =>
        new()
        {
            Id = id,
            Sku = $"WH-SKU-{id}",
            VariantName = "Segun · 6ft",
            IsActive = true,
            PriceOverride = Money.Taka(price),
            ProductId = id,
            Product = new Product
            {
                Id = id,
                Code = $"WH-{id}",
                Name = LocalizedText.Create($"Product {id}"),
                Slug = Slug.From($"product-{id}"),
                BasePrice = Money.Taka(price),
                Status = ProductStatus.Active,
                ProductType = ProductType.Stocked
            }
        };

    private Cart CartWith(
        ProductVariant variant, int quantity = 1, long cartId = 1, long lineId = 1) =>
        new()
        {
            Id = cartId,
            CustomerId = CustomerId,
            Currency = Money.Bdt,
            Status = CartStatus.Active,
            ExpiresAt = _clock.UtcNow.AddDays(CartRules.LifetimeDays),
            Lines =
            [
                new CartLine
                {
                    Id = lineId,
                    CartId = cartId,
                    ProductVariantId = variant.Id,
                    ProductVariant = variant,
                    Quantity = quantity,
                    UnitPriceAtAdd = variant.EffectivePrice
                }
            ]
        };
}
