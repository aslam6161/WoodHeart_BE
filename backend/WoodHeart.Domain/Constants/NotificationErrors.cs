namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for notification delivery.
/// </summary>
/// <remarks>
/// Same contract as the rest: the suffix picks the HTTP status in
/// <c>BaseApiController.HandleResult</c>. These carry the <c>external.</c>
/// prefix, which maps to <b>502</b> — the honest answer when it is somebody
/// else's service that failed, and the one that stops a gateway outage being
/// reported as a bug in the shop.
/// </remarks>
public static class NotificationErrors
{
    /// <summary>The SMS gateway refused, timed out, or could not be reached.</summary>
    /// <remarks>
    /// Shares a code with the payment gateway deliberately — both mean "an
    /// upstream we do not control is down", and the client handles them the
    /// same way.
    /// </remarks>
    public const string SmsUnavailable = "external.sms_gateway_unavailable";

    public const string EmailUnavailable = "external.email_unavailable";

    /// <summary>
    /// A message whose type nothing knows how to render.
    /// </summary>
    /// <remarks>
    /// Suppressed rather than retried. A template that does not exist will not
    /// come into existence on the fourth attempt, and five attempts at a
    /// gateway is five chances to bill for a message nobody can compose.
    /// </remarks>
    public const string NoTemplate = "notifications.no_template";

    /// <summary>Nothing to send to — no usable phone number and no email address.</summary>
    public const string NoRecipient = "notifications.no_recipient";

    /// <summary>A template code nothing in the shop knows how to render.</summary>
    /// <remarks>
    /// The admin screen lists what exists; it cannot conjure a ninth kind of
    /// message, because the words would have to be written in the code that
    /// renders them.
    /// </remarks>
    public const string TemplateNotFound = "notifications.template.not_found";

    public const string MessageNotFound = "notifications.message.not_found";

    /// <summary>
    /// A message that cannot be sent again from the panel.
    /// </summary>
    /// <remarks>
    /// Two cases, and both would mislead rather than help. One still queued is
    /// already coming, so the button would do nothing but look as though it
    /// had. One a worker is holding is moments from the gateway, and resetting
    /// it races that worker into sending the same SMS twice.
    /// </remarks>
    public const string NotResendable = "notifications.not_resendable.conflict";
}
