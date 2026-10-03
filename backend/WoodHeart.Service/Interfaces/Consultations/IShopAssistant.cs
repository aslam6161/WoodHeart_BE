using WoodHeart.Repository;

namespace WoodHeart.Service.Interfaces.Consultations;

/// <summary>
/// What the shop will tell a customer about itself, read from its own settings.
/// </summary>
/// <remarks>
/// Not a written FAQ. Every figure here is the one checkout will actually
/// apply, read from the same settings the rest of the application reads — so a
/// delivery charge the assistant quotes is the charge the customer will be
/// billed, and a shop that changes its returns window has changed it
/// everywhere, including in the thing answering questions at midnight.
/// </remarks>
public record ShopFacts(
    string StoreName,
    string? Phone,
    string? Email,
    decimal DeliveryInsideDhaka,
    decimal DeliveryOutsideDhaka,
    decimal FreeDeliveryThreshold,
    IReadOnlyList<string> PaymentMethods,
    bool ConsultationsOffered,
    string? LeadTime,
    string? Returns,
    string? Warranty,
    string? Payment,
    string? Contact);

/// <summary>One turn of the conversation so far.</summary>
/// <remarks>
/// Sent up by the browser rather than stored. A chat about a wardrobe is not
/// worth a table, a session or a retention policy, and the one place it is
/// genuinely needed — "what about the other one?" — is satisfied by the last
/// few turns travelling with the question.
/// </remarks>
public record ChatTurn(bool FromCustomer, string Text);

/// <summary>
/// Something the assistant thinks the customer might want to do next.
/// </summary>
/// <remarks>
/// <b>A proposal, never an act.</b> The model cannot put anything in a basket;
/// it can only suggest a button, which the customer presses, which runs the
/// same cart code every other button on the site runs. The difference matters
/// the first time a model misreads "I don't want the wardrobe".
/// </remarks>
public record ProposedAction(string Kind, string Label, string? Slug);

/// <summary>
/// The kinds of action the storefront knows how to carry out.
/// </summary>
/// <remarks>
/// A closed list on purpose. The model proposes a string; anything not on this
/// list is dropped before it reaches the browser, so a model that invents
/// <c>cancel_order</c> proposes nothing at all rather than a button nobody
/// wrote.
/// </remarks>
public static class ActionKinds
{
    public const string ViewProduct = "view_product";
    public const string AddToBasket = "add_to_basket";
    public const string ViewBasket = "view_basket";
    public const string Checkout = "checkout";
    public const string TrackOrder = "track_order";
    public const string BookConsultation = "book_consultation";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ViewProduct, AddToBasket, ViewBasket, Checkout, TrackOrder, BookConsultation
    };

    /// <summary>Kinds that mean nothing without a product to point at.</summary>
    public static bool NeedsSlug(string kind) =>
        kind is ViewProduct or AddToBasket;
}

/// <summary>What the model answered: prose, pieces, and what to offer next.</summary>
public record ShopAnswer(
    string Reply,
    IReadOnlyList<string> Slugs,
    IReadOnlyList<ProposedAction> Actions);

/// <summary>
/// The assistant behind the chat window on every storefront page.
/// </summary>
/// <remarks>
/// A separate port from <see cref="IDesignAssistant"/> because it is a
/// different job with a different brief — that one furnishes a room, this one
/// answers "do you deliver to Sylhet" and "how do I pay" — even though the same
/// client and the same model serve both.
/// </remarks>
public interface IShopAssistant
{
    bool IsAvailable { get; }

    Task<GeneralResponse<ShopAnswer>> ChatAsync(
        string question,
        IReadOnlyList<ChatTurn> history,
        ShopFacts facts,
        IReadOnlyList<CataloguePiece> catalogue,
        CancellationToken cancellationToken = default);
}
