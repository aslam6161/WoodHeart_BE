using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Settings;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Consultations;
using WoodHeart.Service.Services.Consultations;

namespace WoodHeart.Tests.Consultations;

/// <summary>
/// The design assistant, and mostly one question: can it tell a customer
/// something about this shop that is not true?
/// </summary>
/// <remarks>
/// A language model will occasionally state a price with total confidence and
/// get it wrong. On a furniture shop's website that is a quotation somebody has
/// to honour or explain. The shape of this service is the answer to that — the
/// model picks <i>which</i> pieces, the database says what they cost — and
/// these tests are what hold that shape in place.
/// </remarks>
public class DesignAdviceServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IDesignAssistant _assistant = Substitute.For<IDesignAssistant>();
    private readonly IFeatureFlagService _features = Substitute.For<IFeatureFlagService>();

    public DesignAdviceServiceTests()
    {
        _assistant.IsAvailable.Returns(true);
        _features.IsEnabledAsync(FeatureFlags.ConsultationsEnabled, Arg.Any<CancellationToken>())
            .Returns(true);
        _products.SearchAsync(Arg.Any<ProductQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<Product>([Bed, Wardrobe], 2, 1, 60));
    }

    private DesignAdviceService CreateService() =>
        new(_products, _assistant, _features, Options.Create(new DesignAssistantSettings
        {
            ApiKey = "test-key"
        }));

    private void Answers(string reply, params string[] slugs) =>
        _assistant.AskAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<CataloguePiece>>(),
                Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<DesignAnswer>.Success(new DesignAnswer(reply, slugs)));

    private static DesignAdviceRequestDto Ask(string question = "What suits a small bedroom?") =>
        new() { Question = question };

    // -------------------------------------------------------------------------
    // Nothing the model says about a product reaches the customer as fact
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_piece_the_model_invented_is_dropped()
    {
        // The failure that matters. A model asked for slugs will occasionally
        // produce a plausible one for furniture this shop does not make, and a
        // card for it would be a product page that 404s — or worse, an order
        // for something nobody can build.
        Answers("Try these.", "segun-king-bed", "italian-marble-throne");

        var result = await CreateService().AskAsync(Ask());

        result.Data!.Products.Count.ShouldBe(1);
        result.Data.Products[0].Slug.ShouldBe("segun-king-bed");
    }

    [Fact]
    public async Task The_prices_come_from_the_database_not_the_answer()
    {
        // The model was told the bed is 45,000 and has said 38,000 in its
        // prose. What the storefront renders is the shop's number.
        Answers("The Segun King Bed is Tk 38,000.", "segun-king-bed");

        var result = await CreateService().AskAsync(Ask());

        result.Data!.Products[0].FromPrice.ShouldBe(45_000m);
    }

    [Fact]
    public async Task The_order_the_model_chose_is_kept()
    {
        // It discussed them in that order, and the prose refers to "the first"
        // and "the second". Re-sorting them would make the answer wrong.
        Answers("The wardrobe first, then the bed.", "four-door-wardrobe", "segun-king-bed");

        var slugs = (await CreateService().AskAsync(Ask())).Data!.Products
            .Select(product => product.Slug).ToList();

        slugs.ShouldBe(["four-door-wardrobe", "segun-king-bed"]);
    }

    [Fact]
    public async Task The_same_piece_twice_is_shown_once()
    {
        Answers("This one, and also this one.", "segun-king-bed", "SEGUN-KING-BED");

        (await CreateService().AskAsync(Ask())).Data!.Products.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Only_what_the_shop_is_selling_is_offered_to_the_model()
    {
        Answers("Here you are.", "segun-king-bed");

        await CreateService().AskAsync(Ask());

        // A draft is half-written copy and a price nobody has approved. It must
        // not reach a third party, and it must certainly not be recommended.
        await _products.Received(1).SearchAsync(
            Arg.Is<ProductQuery>(query => query.Status == ProductStatus.Active),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_model_is_given_no_more_than_the_customer_could_read_on_the_website()
    {
        IReadOnlyList<CataloguePiece>? sent = null;

        _assistant.AskAsync(Arg.Any<string>(), Arg.Do<IReadOnlyList<CataloguePiece>>(c => sent = c),
                Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<DesignAnswer>.Success(new DesignAnswer("Yes.", [])));

        await CreateService().AskAsync(Ask());

        // No internal code, no cost price, no supplier, no stock position. What
        // leaves this building is what is already on the product page.
        sent.ShouldNotBeNull();
        sent!.Count.ShouldBe(2);
        sent[0].Slug.ShouldBe("segun-king-bed");
        sent[0].FromPrice.ShouldBe(45_000m);
    }

    // -------------------------------------------------------------------------
    // When it should not answer at all
    // -------------------------------------------------------------------------

    [Fact]
    public async Task It_is_off_when_consultations_are_off()
    {
        // One switch for both. A shop that has stopped taking design work
        // should not have a page handing out design advice.
        _features.IsEnabledAsync(FeatureFlags.ConsultationsEnabled, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateService().AskAsync(Ask());

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ConsultationErrors.AdviceUnavailable);

        await _assistant.DidNotReceive().AskAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<CataloguePiece>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_is_off_when_no_model_is_configured()
    {
        _assistant.IsAvailable.Returns(false);

        var result = await CreateService().AskAsync(Ask());

        result.ErrorCode.ShouldBe(ConsultationErrors.AdviceUnavailable);
    }

    [Fact]
    public async Task An_empty_catalogue_is_not_handed_to_the_model()
    {
        // With nothing to recommend from, the only answer it can give is an
        // invented one. Better to say there is nothing yet.
        _products.SearchAsync(Arg.Any<ProductQuery>(), Arg.Any<CancellationToken>())
            .Returns(PagedList<Product>.Empty(1, 60));

        var result = await CreateService().AskAsync(Ask());

        result.IsSuccess.ShouldBeFalse();

        await _assistant.DidNotReceive().AskAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<CataloguePiece>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_provider_failure_is_reported_as_the_provider_failing()
    {
        _assistant.AskAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<CataloguePiece>>(),
                Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<DesignAnswer>.Fail(
                ConsultationErrors.AdviceFailed, "The design assistant took too long."));

        var result = await CreateService().AskAsync(Ask());

        // An `external.` code, which the controller turns into a 502. The shop
        // did its part; the customer should be told to try again rather than
        // told they did something wrong.
        result.ErrorCode.ShouldBe(ConsultationErrors.AdviceFailed);
        result.ErrorCode.ShouldStartWith("external.");
    }

    [Fact]
    public async Task The_prose_survives_even_when_no_piece_was_named()
    {
        // "Nothing here suits a nursery, but the designer could help" is a
        // useful answer with no products attached to it.
        Answers("Nothing here quite suits that. Book a consultation and we will make one.");

        var result = await CreateService().AskAsync(Ask());

        result.IsSuccess.ShouldBeTrue();
        result.Data!.Reply.ShouldContain("Book a consultation");
        result.Data.Products.ShouldBeEmpty();
    }

    [Fact]
    public async Task It_will_not_recommend_the_whole_catalogue()
    {
        var many = Enumerable.Range(1, 10)
            .Select(i => NewProduct(i, $"piece-{i}", $"Piece {i}"))
            .ToList();

        _products.SearchAsync(Arg.Any<ProductQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<Product>(many, many.Count, 1, 60));

        Answers("All of them.", many.Select(p => p.Slug.Value).ToArray());

        // A model that lists everything has not made a recommendation, and a
        // customer facing ten cards is back where they started.
        (await CreateService().AskAsync(Ask())).Data!.Products.Count.ShouldBe(4);
    }

    // -------------------------------------------------------------------------

    private static Product Bed => NewProduct(1, "segun-king-bed", "Segun King Bed", 45_000m);

    private static Product Wardrobe => NewProduct(2, "four-door-wardrobe", "Four-Door Wardrobe", 92_000m);

    private static Product NewProduct(long id, string slug, string name, decimal price = 10_000m) => new()
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
