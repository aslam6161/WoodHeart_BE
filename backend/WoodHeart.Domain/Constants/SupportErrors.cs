namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for the contact page and the inbox behind it.
/// </summary>
/// <remarks>
/// Same contract as the rest: the client branches on the code, the message is
/// prose a customer can read, and the suffix picks the HTTP status.
/// </remarks>
public static class SupportErrors
{
    private const string Prefix = "support.";

    /// <summary>
    /// A message with neither a phone number nor an email address on it.
    /// </summary>
    /// <remarks>
    /// Its own code rather than a generic validation failure, because the form
    /// has to say something specific: neither field is required on its own, and
    /// "this field is required" under both of them is wrong twice over.
    /// </remarks>
    public const string NoReplyRoute = Prefix + "contact.no_reply_route";

    /// <summary>The id in the URL matches nothing in the inbox.</summary>
    public const string MessageNotFound = Prefix + "contact_message.not_found";
}
