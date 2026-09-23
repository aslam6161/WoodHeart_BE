namespace WoodHeart.Domain.Enums.Quotations;

/// <summary>
/// Where a quotation stands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Draft is not a hidden Sent.</b> A designer works a quotation up over an
/// afternoon — adding a wardrobe, changing a finish, taking something off —
/// and none of that should reach the customer. Sending it is a separate,
/// deliberate act, and it is the point at which the figures stop moving.
/// </para>
/// <para>
/// <b>Expired is a state, not the absence of one.</b> A quotation has a date
/// it is good until, because timber prices move; past it the shop wants to
/// re-quote rather than honour a figure from three months ago. It is recorded
/// rather than inferred so that "how many quotations went cold" is a question
/// with an answer.
/// </para>
/// </remarks>
public enum QuotationStatus
{
    /// <summary>Being worked up. The customer cannot see it.</summary>
    Draft = 0,

    /// <summary>With the customer, and the figures are now fixed.</summary>
    Sent = 1,

    /// <summary>The customer said yes. Nothing is owed until it becomes an order.</summary>
    Accepted = 2,

    /// <summary>The customer said no.</summary>
    Declined = 3,

    /// <summary>Its date passed without an answer.</summary>
    Expired = 4,

    /// <summary>It became an order. The end of the line.</summary>
    Converted = 5
}
