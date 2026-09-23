namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for quotations.
/// </summary>
/// <remarks>
/// Same contract as the rest: the client branches on the code, the message is
/// prose, and the suffix picks the HTTP status.
/// </remarks>
public static class QuotationErrors
{
    private const string Prefix = "quotations.";

    public const string NotFound = Prefix + "quotation.not_found";

    /// <summary>The move is not one <c>QuotationStatusMachine</c> allows from here.</summary>
    public const string TransitionInvalid = Prefix + "transition_invalid.conflict";

    /// <summary>
    /// Editing one the customer is holding.
    /// </summary>
    /// <remarks>
    /// A conflict rather than a validation failure: the request was
    /// well-formed and the quotation had simply moved on. Pulling it back to
    /// Draft is the way to change it, and that is a deliberate act.
    /// </remarks>
    public const string NotEditable = Prefix + "not_editable.conflict";

    /// <summary>Past the point where the customer can answer it themselves.</summary>
    public const string NotAnswerable = Prefix + "not_answerable.conflict";

    /// <summary>Its date has passed. Re-quote rather than honour it.</summary>
    public const string Expired = Prefix + "expired.conflict";

    /// <summary>Nothing on it, so there is nothing to quote.</summary>
    public const string NoLines = Prefix + "no_lines";

    /// <summary>Sending or converting one with nowhere to deliver to.</summary>
    public const string AddressRequired = Prefix + "address_required";

    /// <summary>A validity date already in the past.</summary>
    public const string ValidUntilInvalid = Prefix + "valid_until_invalid";

    public const string QuantityInvalid = Prefix + "quantity_invalid";

    public const string PriceInvalid = Prefix + "price_invalid";

    /// <summary>A discount larger than the goods it comes off.</summary>
    public const string DiscountTooLarge = Prefix + "discount_too_large";

    public const string DescriptionRequired = Prefix + "description_required";

    /// <summary>A line naming a variant the catalogue does not have.</summary>
    public const string VariantNotFound = Prefix + "variant.not_found";

    public const string ContactPhoneInvalid = Prefix + "contact_phone_invalid";

    /// <summary>It has already become an order.</summary>
    public const string AlreadyConverted = Prefix + "already_converted.conflict";

    /// <summary>The customer has not accepted it yet.</summary>
    public const string NotAccepted = Prefix + "not_accepted.conflict";
}
