using Microsoft.Extensions.Logging;

namespace WoodHeart.Service.Services.Ordering;

/// <summary>
/// Source-generated logging for the cart, in the 1500 block.
/// </summary>
/// <remarks>
/// Only the merge is logged, and deliberately so. Adding and removing items is
/// high-volume and uninteresting; folding a guest basket into a member one is
/// the operation that can lose somebody's items, and "where did my basket go
/// after I signed in" needs an answer that is not a guess.
/// </remarks>
internal static partial class CartLog
{
    [LoggerMessage(
        EventId = 1500,
        Level = LogLevel.Information,
        Message = "Guest cart {CartId} adopted by customer {CustomerId} with {LineCount} line(s).")]
    public static partial void GuestCartAdopted(
        ILogger logger, long cartId, long customerId, int lineCount);

    [LoggerMessage(
        EventId = 1501,
        Level = LogLevel.Information,
        Message = "Guest cart {GuestCartId} merged into cart {CartId}, now {LineCount} line(s).")]
    public static partial void GuestCartMerged(
        ILogger logger, long guestCartId, long cartId, int lineCount);
}
