using Microsoft.Extensions.Options;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Payments;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Consultations;
using WoodHeart.Service.Mapping.Catalog;

namespace WoodHeart.Service.Services.Consultations;

/// <summary>
/// The chat window on every storefront page.
/// </summary>
/// <remarks>
/// <para>
/// Same bargain as the design assistant, widened. The model decides what to
/// say; the database decides what is true. The catalogue it may recommend from
/// is read here, the delivery charge and the returns policy it quotes are the
/// shop's own settings, and the products the customer finally sees are read
/// back by slug — so a piece it invented disappears and a price it misremembers
/// never reaches the page.
/// </para>
/// <para>
/// <b>It cannot do anything.</b> Everything it offers is a proposal with a
/// label, checked here against a closed list of actions the storefront knows
/// how to carry out. Pressing one runs the shop's own cart or router code. A
/// model that misreads "I do not want the wardrobe" can offer the wrong button;
/// it cannot buy a wardrobe.
/// </para>
/// </remarks>
public class ShopChatService(
    IProductRepository products,
    IPaymentMethodConfigRepository paymentMethods,
    IShopAssistant assistant,
    IStoreSettingService settings,
    IFeatureFlagService features,
    IOptions<DesignAssistantSettings> options) : IShopChatService
{
    private readonly DesignAssistantSettings assistantSettings = options.Value;

    /// <summary>
    /// How much of the conversation travels with each question.
    /// </summary>
    /// <remarks>
    /// Six turns is enough for "what about the other one?" and little enough
    /// that a long chat does not quietly become an expensive one — the whole
    /// catalogue is already in every request.
    /// </remarks>
    private const int MaxHistoryTurns = 6;

    /// <summary>Longest remembered turn. A pasted essay is not context.</summary>
    private const int MaxTurnLength = 600;

    /// <summary>Most buttons offered at once. More than two is a menu.</summary>
    private const int MaxActions = 2;

    /// <summary>Most products shown with one answer.</summary>
    private const int MaxProducts = 3;

    public async Task<GeneralResponse<bool>> IsOfferedAsync(
        CancellationToken cancellationToken = default) =>
        GeneralResponse<bool>.Success(
            assistant.IsAvailable
            && await features.IsEnabledAsync(FeatureFlags.ShopAssistantEnabled, cancellationToken));

    public async Task<GeneralResponse<ShopChatReplyDto>> ChatAsync(
        ShopChatRequestDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!assistant.IsAvailable
            || !await features.IsEnabledAsync(FeatureFlags.ShopAssistantEnabled, cancellationToken))
        {
            return GeneralResponse<ShopChatReplyDto>.Fail(
                ConsultationErrors.AdviceUnavailable,
                "The assistant is not available just now.");
        }

        var catalogue = await LoadCatalogueAsync(cancellationToken);

        if (catalogue.Count == 0)
        {
            return GeneralResponse<ShopChatReplyDto>.Fail(
                ConsultationErrors.AdviceUnavailable,
                "There is nothing in the catalogue to talk about yet.");
        }

        var facts = await LoadFactsAsync(cancellationToken);

        var pieces = catalogue.Select(product => new CataloguePiece(
            product.Slug.Value,
            product.Name.En,
            product.Category?.Name.En ?? "Furniture",
            product.BasePrice.Amount,
            product.ProductType == ProductType.MadeToOrder ? "made to order" : "in stock",
            product.ShortDescription?.En)).ToList();

        var answer = await assistant.ChatAsync(
            dto.Message, Trim(dto.History), facts, pieces, cancellationToken);

        if (!answer.IsSuccess || answer.Data is null)
        {
            return GeneralResponse<ShopChatReplyDto>.Fail(
                answer.ErrorCode ?? ConsultationErrors.AdviceFailed, answer.Message);
        }

        return GeneralResponse<ShopChatReplyDto>.Success(new ShopChatReplyDto
        {
            Reply = answer.Data.Reply,
            Products = Resolve(answer.Data.Slugs, catalogue),
            Actions = Allowed(answer.Data.Actions, catalogue)
        });
    }

    /// <summary>
    /// The conversation so far, cut down to what is worth paying for.
    /// </summary>
    /// <remarks>
    /// The browser sends this, so it is a stranger's input: it is capped in
    /// length and in number here rather than trusted, and it is capped before
    /// the model sees it rather than after the bill arrives.
    /// </remarks>
    private static IReadOnlyList<ChatTurn> Trim(IReadOnlyList<ShopChatTurnDto>? history)
    {
        if (history is null || history.Count == 0)
        {
            return [];
        }

        return history
            .Where(turn => !string.IsNullOrWhiteSpace(turn.Text))
            .TakeLast(MaxHistoryTurns)
            .Select(turn => new ChatTurn(
                turn.FromCustomer,
                turn.Text.Length > MaxTurnLength ? turn.Text[..MaxTurnLength] : turn.Text))
            .ToList();
    }

    private async Task<IReadOnlyList<Domain.Entity.Catalog.Product>> LoadCatalogueAsync(
        CancellationToken cancellationToken)
    {
        var page = await products.SearchAsync(
            new ProductQuery
            {
                PageNumber = 1,
                PageSize = assistantSettings.MaxCatalogueItems,
                Status = ProductStatus.Active,
                SortBy = ProductSort.RecentlyPublished
            },
            cancellationToken);

        return page.ToList();
    }

    /// <summary>
    /// What the shop says about itself, read from what it actually does.
    /// </summary>
    /// <remarks>
    /// The payment methods are the ones switched on in the admin, not a list in
    /// a prompt — so the day bKash goes live the assistant starts saying so,
    /// and the day it is switched off it stops. That is the difference between
    /// an FAQ and a fact.
    /// </remarks>
    private async Task<ShopFacts> LoadFactsAsync(CancellationToken cancellationToken)
    {
        var enabled = await paymentMethods.GetEnabledAsync(cancellationToken);

        return new ShopFacts(
            await settings.GetStringAsync(SettingKeys.StoreName, cancellationToken) ?? "WoodHeart",
            await settings.GetStringAsync(SettingKeys.StorePhone, cancellationToken),
            await settings.GetStringAsync(SettingKeys.StoreEmail, cancellationToken),
            await settings.GetDecimalAsync(SettingKeys.DeliveryChargeInsideDhaka, 0m, cancellationToken),
            await settings.GetDecimalAsync(SettingKeys.DeliveryChargeOutsideDhaka, 0m, cancellationToken),
            await settings.GetDecimalAsync(SettingKeys.FreeDeliveryThreshold, 0m, cancellationToken),
            enabled.Select(method => method.DisplayName.En).ToList(),
            await features.IsEnabledAsync(FeatureFlags.ConsultationsEnabled, cancellationToken),
            await settings.GetStringAsync(SettingKeys.FaqLeadTime, cancellationToken),
            await settings.GetStringAsync(SettingKeys.FaqReturns, cancellationToken),
            await settings.GetStringAsync(SettingKeys.FaqWarranty, cancellationToken),
            await settings.GetStringAsync(SettingKeys.FaqPayment, cancellationToken),
            await settings.GetStringAsync(SettingKeys.FaqContact, cancellationToken));
    }

    private static IReadOnlyList<DTOs.Catalog.StorefrontProductDto> Resolve(
        IReadOnlyList<string> slugs,
        IReadOnlyList<Domain.Entity.Catalog.Product> catalogue)
    {
        var bySlug = catalogue.ToDictionary(
            product => product.Slug.Value, StringComparer.OrdinalIgnoreCase);

        return slugs
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(bySlug.GetValueOrDefault)
            .Where(product => product is not null)
            .Take(MaxProducts)
            .Select(product => CatalogMapper.ToStorefront(product!))
            .ToList();
    }

    /// <summary>
    /// The buttons the storefront will actually honour.
    /// </summary>
    /// <remarks>
    /// Three rules, each guarding a different mistake. A kind not on the closed
    /// list is dropped, so a model that invents <c>cancel_order</c> offers
    /// nothing. A product action naming a slug the shop does not sell is
    /// dropped, so an invented piece cannot become a button that 404s. And a
    /// button with no words on it is dropped, because a customer cannot press
    /// what they cannot read.
    /// </remarks>
    private static IReadOnlyList<ShopChatActionDto> Allowed(
        IReadOnlyList<ProposedAction> proposed,
        IReadOnlyList<Domain.Entity.Catalog.Product> catalogue)
    {
        var slugs = catalogue
            .Select(product => product.Slug.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return proposed
            .Where(action => ActionKinds.All.Contains(action.Kind))
            .Where(action => !string.IsNullOrWhiteSpace(action.Label))
            .Where(action => !ActionKinds.NeedsSlug(action.Kind)
                || (action.Slug is not null && slugs.Contains(action.Slug)))
            .Take(MaxActions)
            .Select(action => new ShopChatActionDto
            {
                Kind = action.Kind,
                Label = action.Label,
                Slug = action.Slug
            })
            .ToList();
    }
}
