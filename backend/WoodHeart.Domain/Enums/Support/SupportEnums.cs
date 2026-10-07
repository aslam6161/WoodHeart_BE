namespace WoodHeart.Domain.Enums.Support;

/// <summary>
/// What a message is about, chosen by the person writing it.
/// </summary>
/// <remarks>
/// <para>
/// Six values, because a list long enough to need scrolling gets answered with
/// whatever is at the top. These are the things people actually write in about,
/// and the one that earns its place loudest is <see cref="Complaint"/>: a shop
/// that cannot see its complaints separately from its enquiries finds them a
/// week late, underneath them.
/// </para>
/// <para>
/// It is a hint for triage, never a routing decision. Nothing is hidden or
/// deprioritised because of what somebody picked from a dropdown.
/// </para>
/// </remarks>
public enum ContactTopic
{
    /// <summary>Anything that is not one of the others. The default.</summary>
    General = 0,

    /// <summary>An order already placed — where it is, changing it, cancelling it.</summary>
    Order = 1,

    /// <summary>Delivery: charges, timing, a rider who has not arrived.</summary>
    Delivery = 2,

    /// <summary>A piece in the catalogue: sizes, timber, finishes, whether it can be made to order.</summary>
    Product = 3,

    /// <summary>Design work, a site visit, or a quotation.</summary>
    Consultation = 4,

    /// <summary>Something went wrong. Separated so it cannot be lost in the pile.</summary>
    Complaint = 5
}

/// <summary>
/// Where a message stands in the shop's inbox.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Read"/> is not <see cref="Answered"/>.</b> Opening a message
/// and replying to it are different acts, and collapsing them is how a shop
/// ends up believing it has answered everybody. The count the inbox shows is of
/// <see cref="New"/> — what nobody has looked at yet.
/// </para>
/// <para>
/// <b><see cref="Spam"/> rather than delete.</b> A public form on the open
/// internet collects junk, and junk has to leave the list or the list stops
/// being read. It is marked rather than destroyed, because the one that matters
/// is the real customer somebody marked as junk by accident.
/// </para>
/// </remarks>
public enum ContactMessageStatus
{
    /// <summary>Nobody has opened it.</summary>
    New = 0,

    /// <summary>Somebody has read it. Still owed a reply.</summary>
    Read = 1,

    /// <summary>Replied to, by whatever means the customer left.</summary>
    Answered = 2,

    /// <summary>Junk. Out of the list, still on the table.</summary>
    Spam = 3
}
