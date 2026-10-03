using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Settings;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Payments;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Consultations;
using WoodHeart.Service.Services.Consultations;

namespace WoodHeart.Tests.Consultations;

/// <summary>
/// The chat window, and the one question worth asking of it: what can it do to
/// this shop?
/// </summary>
/// <remarks>
/// The answer is meant to be "nothing". It proposes buttons; the storefront
/// carries them out. These tests hold the gate between the two — every button
/// that reaches a customer has a kind the storefront implements and, where it
/// names a product, a product this shop actually sells.
/// </remarks>
public class ShopChatServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IPaymentMethodConfigRepository _methods =
        Substitute.For<IPaymentMethodConfigRepository>();
    private readonly IShopAssistant _assistant = Substitute.For<IShopAssistant>();
    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();
    private readonly IFeatureFlagService _features = Substitute.For<IFeatureFlagService>();

    public ShopChatServiceTests()
    {
        _assistant.IsAvailable.Returns(true);
        _features.IsEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _products.SearchAsync(Arg.Any<ProductQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<Product>([Bed, Wardrobe], 2, 1, 60));
        _methods.GetEnabledAsync(Arg.Any<CancellationToken>()).Returns([
            new PaymentMethodConfig
            {
                Code = "cod",
                DisplayName = LocalizedText.Create("Cash on delivery"),
                IsEnabled = true
            }
        ]);
        _settings.GetStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        _settings.GetDecimalAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(0m);
    }

    private ShopChatService CreateService() =>
        new(_products, _methods, _assistant, _settings, _features,
            Options.Create(new DesignAssistantSettings { ApiKey = "test-key" }));

    private void Answers(string reply, IReadOnlyList<string>? slugs = null,
        params ProposedAction[] actions) =>
        _assistant.ChatAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Any<ShopFacts>(), Arg.Any<IReadOnlyList<CataloguePiece>>(),
                Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<ShopAnswer>.Success(
                new ShopAnswer(reply, slugs ?? [], actions)));

    private static ShopChatRequestDto Ask(string message = "Do you have a wardrobe?") =>
        new() { Message = message };

    // -------------------------------------------------------------------------
    // The gate between proposing and doing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_button_the_storefront_cannot_carry_out_never_reaches_it()
    {
        // The failure this gate exists for. A model that decides it can cancel
        // an order proposes a button nobody wrote, and a button nobody wrote is
        // either dead or — worse — wired to the nearest thing that matches.
        Answers("Done.", [], new ProposedAction("cancel_order", "Cancel my order", null));

        var result = await CreateService().ChatAsync(Ask());

        result.Data!.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_button_for_a_product_the_shop_does_not_sell_is_dropped()
    {
        // Otherwise an invented piece becomes a button that 404s, with the
        // shop's own assistant having sent the customer there.
        Answers("Try this.", [],
            new ProposedAction(ActionKinds.AddToBasket, "Add it", "italian-marble-throne"));

        (await CreateService().ChatAsync(Ask())).Data!.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_product_button_with_no_product_is_dropped()
    {
        Answers("Here.", [], new ProposedAction(ActionKinds.ViewProduct, "See it", null));

        (await CreateService().ChatAsync(Ask())).Data!.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_button_with_nothing_written_on_it_is_dropped()
    {
        // A customer cannot press what they cannot read.
        Answers("Here.", [], new ProposedAction(ActionKinds.ViewBasket, "   ", null));

        (await CreateService().ChatAsync(Ask())).Data!.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_buttons_that_are_allowed_come_through_intact()
    {
        Answers("Here it is.", ["four-door-wardrobe"],
            new ProposedAction(ActionKinds.ViewProduct, "See the wardrobe", "four-door-wardrobe"),
            new ProposedAction(ActionKinds.TrackOrder, "Track an order", null));

        var actions = (await CreateService().ChatAsync(Ask())).Data!.Actions;

        actions.Count.ShouldBe(2);
        actions[0].Kind.ShouldBe(ActionKinds.ViewProduct);
        actions[0].Slug.ShouldBe("four-door-wardrobe");
        actions[1].Kind.ShouldBe(ActionKinds.TrackOrder);
    }

    [Fact]
    public async Task Three_buttons_is_a_menu_so_only_two_are_offered()
    {
        Answers("Lots of options.", [],
            new ProposedAction(ActionKinds.ViewBasket, "Basket", null),
            new ProposedAction(ActionKinds.Checkout, "Checkout", null),
            new ProposedAction(ActionKinds.TrackOrder, "Track", null));

        (await CreateService().ChatAsync(Ask())).Data!.Actions.Count.ShouldBe(2);
    }

    // -------------------------------------------------------------------------
    // The same bargain as everywhere else: the database says what is true
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_product_the_model_invented_is_not_shown()
    {
        Answers("These two.", ["segun-king-bed", "italian-marble-throne"]);

        var products = (await CreateService().ChatAsync(Ask())).Data!.Products;

        products.Count.ShouldBe(1);
        products[0].Slug.ShouldBe("segun-king-bed");
    }

    [Fact]
    public async Task The_price_shown_is_the_database_price()
    {
        Answers("The bed is Tk 30,000.", ["segun-king-bed"]);

        (await CreateService().ChatAsync(Ask())).Data!.Products[0].FromPrice.ShouldBe(45_000m);
    }

    [Fact]
    public async Task Only_active_products_are_put_in_front_of_the_model()
    {
        Answers("Here.", []);

        await CreateService().ChatAsync(Ask());

        await _products.Received(1).SearchAsync(
            Arg.Is<ProductQuery>(query => query.Status == ProductStatus.Active),
            Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // What travels, and what it costs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Only_the_tail_of_a_long_conversation_is_sent()
    {
        IReadOnlyList<ChatTurn>? sent = null;

        _assistant.ChatAsync(Arg.Any<string>(), Arg.Do<IReadOnlyList<ChatTurn>>(h => sent = h),
                Arg.Any<ShopFacts>(), Arg.Any<IReadOnlyList<CataloguePiece>>(),
                Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<ShopAnswer>.Success(new ShopAnswer("Yes.", [], [])));

        var history = Enumerable.Range(1, 20)
            .Select(i => new ShopChatTurnDto { FromCustomer = i % 2 == 1, Text = $"turn {i}" })
            .ToList();

        await CreateService().ChatAsync(new ShopChatRequestDto { Message = "And?", History = history });

        // The whole catalogue is already in every request; an unbounded history
        // on top of it is an unbounded bill. The newest turns are the ones that
        // make "what about the other one?" work.
        sent.ShouldNotBeNull();
        sent!.Count.ShouldBe(6);
        sent[^1].Text.ShouldBe("turn 20");
    }

    [Fact]
    public async Task A_pasted_essay_is_cut_before_it_is_paid_for()
    {
        IReadOnlyList<ChatTurn>? sent = null;

        _assistant.ChatAsync(Arg.Any<string>(), Arg.Do<IReadOnlyList<ChatTurn>>(h => sent = h),
                Arg.Any<ShopFacts>(), Arg.Any<IReadOnlyList<CataloguePiece>>(),
                Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<ShopAnswer>.Success(new ShopAnswer("Yes.", [], [])));

        await CreateService().ChatAsync(new ShopChatRequestDto
        {
            Message = "And?",
            History = [new ShopChatTurnDto { FromCustomer = true, Text = new string('x', 5_000) }]
        });

        sent![0].Text.Length.ShouldBe(600);
    }

    [Fact]
    public async Task The_payment_methods_it_quotes_are_the_ones_switched_on()
    {
        ShopFacts? facts = null;

        _assistant.ChatAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Do<ShopFacts>(f => facts = f), Arg.Any<IReadOnlyList<CataloguePiece>>(),
                Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<ShopAnswer>.Success(new ShopAnswer("Cash on delivery.", [], [])));

        await CreateService().ChatAsync(Ask("How can I pay?"));

        // Not a list in a prompt. The day bKash is switched on in the admin the
        // assistant starts saying so, and the day it is switched off it stops.
        facts.ShouldNotBeNull();
        facts!.PaymentMethods.ShouldBe(["Cash on delivery"]);
    }

    // -------------------------------------------------------------------------
    // When it should not answer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task It_is_off_when_the_flag_is_off()
    {
        _features.IsEnabledAsync(FeatureFlags.ShopAssistantEnabled, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateService().ChatAsync(Ask());

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ConsultationErrors.AdviceUnavailable);

        await _assistant.DidNotReceive().ChatAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<ShopFacts>(),
            Arg.Any<IReadOnlyList<CataloguePiece>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_is_off_when_no_model_is_configured()
    {
        _assistant.IsAvailable.Returns(false);

        (await CreateService().IsOfferedAsync()).Data.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------

    private static Product Bed => NewProduct(1, "segun-king-bed", "Segun King Bed", 45_000m);

    private static Product Wardrobe => NewProduct(2, "four-door-wardrobe", "Four-Door Wardrobe", 92_000m);

    private static Product NewProduct(long id, string slug, string name, decimal price) => new()
    {
        Id = id,
        Code = $"WH-{id:000}",
        Name = LocalizedText.Create(name),
        Slug = Slug.From(slug),
        CategoryId = 20,
        Category = new Category
        {
            Id = 20,
            Name = LocalizedText.Create("Beds"),
            Slug = Slug.From("beds"),
            MaterializedPath = "/2/20/"
        },
        BasePrice = Money.Taka(price),
        Status = ProductStatus.Active,
        SeoTitle = name
    };
}
