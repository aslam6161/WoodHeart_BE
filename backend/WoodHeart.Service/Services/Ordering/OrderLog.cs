using Microsoft.Extensions.Logging;

namespace WoodHeart.Service.Services.Ordering;

/// <summary>
/// Source-generated logging for orders, in the 1600 block.
/// </summary>
/// <remarks>
/// Three events, all of which change money or the customer's expectations.
/// Reads are not logged — an order detail page is fetched constantly and
/// logging it would bury the three lines that matter.
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
}
