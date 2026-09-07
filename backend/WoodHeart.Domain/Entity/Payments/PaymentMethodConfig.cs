using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Payments;

/// <summary>
/// One payment method, as the shop has configured it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This row is the "admin can configure the payment system" requirement.</b>
/// Which methods a customer sees, in what order, under what conditions, with
/// what credentials and against sandbox or live — all of it is data. Turning
/// bKash on the day the merchant account is approved is a toggle, not a
/// release.
/// </para>
/// <para>
/// A row here does not conjure a provider. <see cref="Code"/> must match an
/// <c>IPaymentProvider</c> registered in DI, and the resolver returns only
/// methods where both exist. So an admin cannot enable a gateway nobody has
/// written yet, and a provider left in the code cannot start taking money
/// because someone forgot to disable it.
/// </para>
/// <para>
/// <b><see cref="Credentials"/> never leaves the server.</b> It is encrypted at
/// rest with ASP.NET Data Protection and is not on any DTO — not even the
/// admin's. An admin screen shows "configured" or "not configured" and offers
/// to replace, because a screen that displays a live merchant secret is one
/// screenshot away from being a public one.
/// </para>
/// </remarks>
public class PaymentMethodConfig : BaseEntity
{
    /// <summary><c>cod</c>, <c>bkash</c>, <c>nagad</c>, <c>sslcommerz</c>.</summary>
    public string Code { get; set; } = null!;

    /// <summary>What the customer sees at checkout, in both languages.</summary>
    public LocalizedText DisplayName { get; set; } = null!;

    /// <summary>"Pay the rider in cash when your order arrives."</summary>
    public LocalizedText? Description { get; set; }

    public string? IconUrl { get; set; }

    /// <summary>
    /// Off hides the method from checkout entirely.
    /// </summary>
    /// <remarks>
    /// bKash ships disabled and stays that way until the merchant account is
    /// approved. Building it behind a flag is what makes that day a toggle
    /// rather than a deployment.
    /// </remarks>
    public bool IsEnabled { get; set; }

    public int SortOrder { get; set; }

    public PaymentMode Mode { get; set; } = PaymentMode.Sandbox;

    // --- Eligibility ---------------------------------------------------------

    /// <summary>
    /// Below this the method is hidden. Null means no floor.
    /// </summary>
    /// <remarks>
    /// A gateway's fixed fee makes small orders unprofitable to take
    /// electronically.
    /// </remarks>
    public Money? MinOrderAmount { get; set; }

    /// <summary>
    /// Above this the method is hidden. Null means no ceiling.
    /// </summary>
    /// <remarks>
    /// <b>This is the one that matters for cash on delivery.</b> Sending a rider
    /// out to collect 180,000৳ in notes is a risk the shop may not want, and
    /// this is where it says so.
    /// </remarks>
    public Money? MaxOrderAmount { get; set; }

    /// <summary>
    /// Whether the method is offered for a Dhaka delivery.
    /// </summary>
    /// <remarks>
    /// Two booleans rather than a list of zones, because there are exactly two
    /// zones and a list would be a JSON column to encode one bit. If the country
    /// is ever split more finely, this becomes a table — but not before.
    /// </remarks>
    public bool AvailableInsideDhaka { get; set; } = true;

    /// <summary>Whether it is offered outside Dhaka. COD often is not, at distance.</summary>
    public bool AvailableOutsideDhaka { get; set; } = true;

    // --- Charging ------------------------------------------------------------

    public PaymentChargeType ChargeType { get; set; } = PaymentChargeType.None;

    /// <summary>
    /// Taka for <see cref="PaymentChargeType.Fixed"/>, percent for
    /// <see cref="PaymentChargeType.Percent"/>.
    /// </summary>
    public decimal ChargeValue { get; set; }

    // --- Secrets -------------------------------------------------------------

    /// <summary>
    /// The gateway's credentials, encrypted. Null for methods that need none.
    /// </summary>
    /// <remarks>
    /// A JSON blob rather than columns, because every gateway wants a different
    /// set — an app key and secret here, a username and password there — and a
    /// schema change per gateway is a schema change for something that is
    /// already configuration.
    /// </remarks>
    public string? Credentials { get; set; }
}
