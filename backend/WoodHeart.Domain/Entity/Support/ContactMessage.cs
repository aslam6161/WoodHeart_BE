using WoodHeart.Domain.Enums.Support;

namespace WoodHeart.Domain.Entity.Support;

/// <summary>
/// Something a customer wrote to the shop from the contact page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored, not emailed.</b> A contact form that posts into an SMTP server is
/// a form whose delivery nobody can see: it fails into a log, or into a spam
/// folder, and the customer is told it was sent either way. A row in this table
/// is a message that exists whether or not any other system is working, and the
/// inbox that reads it is part of the same application.
/// </para>
/// <para>
/// <b>Either a phone or an email, never neither.</b> The form takes both and
/// insists on one, because a message the shop cannot answer is not an enquiry —
/// it is a complaint nobody will ever hear the end of. The service enforces it
/// rather than the column, since "at least one of these two" is not something a
/// NOT NULL can say.
/// </para>
/// <para>
/// <b>Why the reply is not kept here.</b> Replies happen on the telephone, over
/// WhatsApp, or in an email client — wherever the customer can actually be
/// reached. Recording the fact of an answer is honest; storing a transcript of
/// a conversation that happened elsewhere would not be.
/// </para>
/// </remarks>
public class ContactMessage : BaseEntity
{
    /// <summary>What they are called. Not verified, and not meant to be.</summary>
    public string Name { get; set; } = null!;

    /// <summary>E.164, normalised on the way in. Null when they left only an email.</summary>
    public string? Phone { get; set; }

    /// <summary>Lower-cased on the way in. Null when they left only a phone number.</summary>
    public string? Email { get; set; }

    public ContactTopic Topic { get; set; } = ContactTopic.General;

    /// <summary>
    /// An order, quotation or booking number they quoted, if any.
    /// </summary>
    /// <remarks>
    /// Free text and deliberately unlinked. Somebody writing in about an order
    /// usually half-remembers the number, and a foreign key to a row that does
    /// not exist would refuse the message rather than let staff work it out.
    /// </remarks>
    public string? Reference { get; set; }

    public string Message { get; set; } = null!;

    public ContactMessageStatus Status { get; set; } = ContactMessageStatus.New;

    /// <summary>When somebody marked it answered. Null until they did.</summary>
    public DateTimeOffset? AnsweredAt { get; set; }

    /// <summary>Which staff account marked it answered.</summary>
    public long? AnsweredBy { get; set; }

    /// <summary>What staff wrote to each other about it — never shown to the customer.</summary>
    public string? StaffNote { get; set; }
}
