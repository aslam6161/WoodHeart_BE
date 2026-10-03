using Microsoft.Extensions.Options;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Interfaces.Consultations;
using WoodHeart.Service.Mapping.Catalog;

namespace WoodHeart.Service.Services.Consultations;

/// <summary>
/// The design assistant, grounded in the shop's actual catalogue.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retrieval, then generation, then retrieval again.</b> The catalogue is
/// read from the database and put in front of the model; the model answers in
/// prose and names pieces by slug; and the pieces the customer finally sees are
/// read from the database a second time, by those slugs.
/// </para>
/// <para>
/// That last step is the point. A model will occasionally state a price
/// confidently and wrongly, and a wrong price on a furniture shop's website is
/// a quotation the shop has to honour or explain. Here it cannot: the prose
/// never carries the figures, the cards beside it are the same ones the rest of
/// the storefront renders, and a slug the model invented matches nothing and is
/// silently dropped.
/// </para>
/// <para>
/// <b>It is switched off by the same flag as consultations.</b> A shop that has
/// stopped taking design work should not have a page offering design advice,
/// and one switch for both means it cannot be half off.
/// </para>
/// </remarks>
public class DesignAdviceService(
    IProductRepository products,
    IDesignAssistant assistant,
    IFeatureFlagService features,
    IOptions<DesignAssistantSettings> options) : IDesignAdviceService
{
    private readonly DesignAssistantSettings settings = options.Value;

    public async Task<GeneralResponse<bool>> IsOfferedAsync(
        CancellationToken cancellationToken = default)
    {
        var offered = assistant.IsAvailable
            && await features.IsEnabledAsync(FeatureFlags.ConsultationsEnabled, cancellationToken);

        return GeneralResponse<bool>.Success(offered);
    }

    public async Task<GeneralResponse<DesignAdviceDto>> AskAsync(
        DesignAdviceRequestDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!assistant.IsAvailable
            || !await features.IsEnabledAsync(FeatureFlags.ConsultationsEnabled, cancellationToken))
        {
            return GeneralResponse<DesignAdviceDto>.Fail(
                ConsultationErrors.AdviceUnavailable,
                "The design assistant is not available just now. You can still book a consultation.");
        }

        var catalogue = await LoadCatalogueAsync(cancellationToken);

        if (catalogue.Count == 0)
        {
            // Nothing to recommend from. Asking the model anyway invites it to
            // invent a catalogue, which is the one failure that matters here.
            return GeneralResponse<DesignAdviceDto>.Fail(
                ConsultationErrors.AdviceUnavailable,
                "There is nothing in the catalogue to suggest from yet.");
        }

        var pieces = catalogue
            .Select(product => new CataloguePiece(
                product.Slug.Value,
                product.Name.En,
                product.Category?.Name.En ?? "Furniture",
                product.BasePrice.Amount,
                product.ProductType == ProductType.MadeToOrder ? "made to order" : "in stock",
                product.ShortDescription?.En))
            .ToList();

        var answer = await assistant.AskAsync(dto.Question, pieces, cancellationToken);

        if (!answer.IsSuccess || answer.Data is null)
        {
            return GeneralResponse<DesignAdviceDto>.Fail(
                answer.ErrorCode ?? ConsultationErrors.AdviceFailed, answer.Message);
        }

        return GeneralResponse<DesignAdviceDto>.Success(new DesignAdviceDto
        {
            Reply = answer.Data.Reply,
            Products = Resolve(answer.Data.Slugs, catalogue)
        });
    }

    /// <summary>
    /// What the shop is actually selling, newest first.
    /// </summary>
    /// <remarks>
    /// Active products only, through the ordinary catalogue query — so a draft
    /// cannot be recommended, and the adviser can only ever point at something
    /// a customer could already have found by browsing.
    /// </remarks>
    private async Task<IReadOnlyList<Domain.Entity.Catalog.Product>> LoadCatalogueAsync(
        CancellationToken cancellationToken)
    {
        var page = await products.SearchAsync(
            new ProductQuery
            {
                PageNumber = 1,
                PageSize = settings.MaxCatalogueItems,
                Status = ProductStatus.Active,
                SortBy = ProductSort.RecentlyPublished
            },
            cancellationToken);

        return page.ToList();
    }

    /// <summary>
    /// The model's slugs, turned back into products the shop can stand behind.
    /// </summary>
    /// <remarks>
    /// Matched against the list the model was given, not queried afresh:
    /// anything it invented matches nothing and disappears, and the order it
    /// chose is kept, because it discussed them in that order. Capped, because
    /// a model that lists the whole catalogue has not made a recommendation.
    /// </remarks>
    private static IReadOnlyList<DTOs.Catalog.StorefrontProductDto> Resolve(
        IReadOnlyList<string> slugs,
        IReadOnlyList<Domain.Entity.Catalog.Product> catalogue)
    {
        const int Most = 4;

        var bySlug = catalogue.ToDictionary(product => product.Slug.Value, StringComparer.OrdinalIgnoreCase);

        return slugs
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(slug => bySlug.GetValueOrDefault(slug))
            .Where(product => product is not null)
            .Take(Most)
            .Select(product => CatalogMapper.ToStorefront(product!))
            .ToList();
    }
}
