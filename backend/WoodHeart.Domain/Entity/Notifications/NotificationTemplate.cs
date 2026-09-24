namespace WoodHeart.Domain.Entity.Notifications;

/// <summary>
/// One kind of message the shop sends, and whether it is sending it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The row is the switch, not the words.</b> The wording lives in
/// <c>NotificationTemplates</c> in code, and deliberately: an SMS here is
/// billed per part, 160 characters in English but only <b>70</b> once a single
/// Bangla character appears. Every message in that class is written to fit the
/// smallest number of parts that still says the thing, in two languages, with
/// the shop's telephone number on the end. A text box on an admin screen would
/// let a good afternoon's editing triple the gateway invoice with nothing
/// anywhere to say it had.
/// </para>
/// <para>
/// What an admin genuinely needs is the decision the wording cannot make for
/// them — <em>do we send this at all, and by which channel</em>. A shop
/// watching its SMS spend may want the quotation follow-up by email only; a
/// shop with no mail server wants the reverse. So those two are data, and the
/// admin screen shows the exact message each template would produce, in both
/// languages, with its part count beside it.
/// </para>
/// <para>
/// <b>A row here does not invent a message.</b> <see cref="Code"/> must match
/// one of <c>NotificationTemplates.KnownTypes</c> — the same relationship
/// <c>PaymentMethodConfig.Code</c> has with its provider. A code with nothing
/// to render it would read as configured on the screen and produce silence in
/// the outbox.
/// </para>
/// </remarks>
public class NotificationTemplate : BaseEntity
{
    /// <summary><c>order.placed</c>, <c>booking.reminder</c>, <c>stock.low</c>.</summary>
    public string Code { get; set; } = null!;

    /// <summary>
    /// Whether this message goes out by SMS.
    /// </summary>
    /// <remarks>
    /// The expensive channel and the one nearly every customer here reads. Off
    /// is a real choice — it is how a shop stops paying for a message it has
    /// decided is not worth a part — and the dispatcher records the skip as
    /// <c>Suppressed</c> with the reason, so a message that never went is not
    /// mistaken for one that failed.
    /// </remarks>
    public bool SmsEnabled { get; set; } = true;

    /// <summary>
    /// Whether this message goes out by email.
    /// </summary>
    /// <remarks>
    /// Free, and read by a minority of customers in this market. On by default
    /// for that reason: it costs nothing and occasionally it is the only copy
    /// somebody keeps.
    /// </remarks>
    public bool EmailEnabled { get; set; } = true;
}
