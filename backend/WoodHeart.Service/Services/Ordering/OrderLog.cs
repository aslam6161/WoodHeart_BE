using Microsoft.Extensions.Logging;

namespace WoodHeart.Service.Services.Ordering;

/// <summary>
/// Source-generated logging for orders, in the 1600 block.
/// </summary>
/// <remarks>
/// Events that change money or the customer's expectations, plus the one
/// read worth a line. An order detail page is fetched constantly and logging
/// it would bury everything else.
/// </remarks>
internal static partial class OrderLog
{
    [LoggerMessage(
        EventId = 1600,
        Level = LogLevel.Information,
        Message = "Order {OrderNumber} placed for {Amount} by {PaymentMethod}.")]
    public static partial void OrderPlaced(
        ILogger logger, string orderNumber, decimal amount, string paymentMethod);

    [LoggerMessage(
        EventId = 1601,
        Level = LogLevel.Information,
        Message = "Order {OrderNumber} moved {FromStatus} -> {ToStatus} by {Actor}.")]
    public static partial void StatusChanged(
        ILogger logger, string orderNumber, string fromStatus, string toStatus, string actor);

    /// <summary>
    /// A guest's past orders being attached to a new account.
    /// </summary>
    /// <remarks>
    /// Logged because it is the one operation here that hands one person's
    /// order history to an account, on the strength of a phone number. If that
    /// ever goes wrong, this line is where the question starts.
    /// </remarks>
    [LoggerMessage(
        EventId = 1602,
        Level = LogLevel.Information,
        Message = "Claimed {Count} guest order(s) for customer {CustomerId}.")]
    public static partial void GuestOrdersClaimed(ILogger logger, int count, long customerId);

    [LoggerMessage(
        EventId = 1603,
        Level = LogLevel.Information,
        Message = "Order {OrderNumber} payment moved {FromStatus} -> {ToStatus} by {Actor}.")]
    public static partial void PaymentRecorded(
        ILogger logger, string orderNumber, string fromStatus, string toStatus, string actor);

    /// <summary>
    /// A member of staff changing what a customer is charged.
    /// </summary>
    /// <remarks>
    /// Logged at Warning rather than Information, and not because anything has
    /// gone wrong. This is the one operation in the shop where one person can
    /// move money on somebody else's order, and it should be visible in a log
    /// filtered to warnings without anybody having to know to look for it.
    /// </remarks>
    [LoggerMessage(
        EventId = 1604,
        Level = LogLevel.Warning,
        Message = "Order {OrderNumber} delivery charge changed {From} -> {To} by {Actor}.")]
    public static partial void DeliveryFeeOverridden(
        ILogger logger, string orderNumber, decimal from, decimal to, string actor);

    /// <summary>
    /// An invoice being drawn.
    /// </summary>
    /// <remarks>
    /// The one read in this file, and it earns the exception: "how many times
    /// was this invoice reprinted, and when" is asked during a dispute, and the
    /// document is not stored anywhere for anyone to count.
    /// </remarks>
    [LoggerMessage(
        EventId = 1605,
        Level = LogLevel.Information,
        Message = "Invoice for order {OrderNumber} rendered, {Bytes} bytes.")]
    public static partial void InvoiceRendered(ILogger logger, string orderNumber, int bytes);
}
