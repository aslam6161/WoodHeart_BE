namespace WoodHeart.Domain.Enums.Payments;

/// <summary>
/// Whether a payment method is pointed at the gateway's sandbox or at real
/// money.
/// </summary>
/// <remarks>
/// Stored per method rather than inferred from the hosting environment. Staging
/// sometimes needs to run a live-mode test against a real merchant account, and
/// a shop that has just been approved switches bKash to Live from an admin
/// screen at 11pm — not by redeploying.
/// </remarks>
public enum PaymentMode
{
    Sandbox = 0,

    Live = 1
}

/// <summary>
/// How a payment method adds to the bill, if it does.
/// </summary>
/// <remarks>
/// Cash on delivery commonly carries a small handling charge in this market,
/// and gateways take a percentage. Both shapes exist so the shop can express
/// either without a code change.
/// </remarks>
public enum PaymentChargeType
{
    /// <summary>No charge. The default, and what COD starts as.</summary>
    None = 0,

    /// <summary>A flat amount, e.g. 50৳ for cash handling.</summary>
    Fixed = 1,

    /// <summary>A percentage of the order total, e.g. 1.5% for a card gateway.</summary>
    Percent = 2
}
