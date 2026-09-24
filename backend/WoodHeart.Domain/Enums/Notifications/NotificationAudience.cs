namespace WoodHeart.Domain.Enums.Notifications;

/// <summary>Who a message is written for.</summary>
/// <remarks>
/// Worth distinguishing on the admin screen, because the two go wrong in
/// opposite ways. A customer message turned off means somebody is never told
/// what happened to their money; a shop message turned off means the shop
/// stops hearing about its own stock, and nobody outside notices at all.
/// </remarks>
public enum NotificationAudience
{
    Customer = 0,

    Shop = 1
}
