using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Payments;

namespace WoodHeart.Service.DTOs.Payments;

/// <summary>
/// One payment method as the admin screen shows it.
/// </summary>
/// <remarks>
/// <b>The credentials are not here and never will be.</b> A screen that
/// displays a live merchant secret is one screenshot away from being a public
/// one, so the admin is told whether something is stored and offered to replace
/// it — see <see cref="HasCredentials"/> and <see cref="CredentialsUnreadable"/>.
/// </remarks>
public class PaymentMethodConfigDto
{
    public long Id { get; init; }

    /// <summary><c>cod</c>, <c>bkash</c>. Fixed: it is the join to the provider.</summary>
    public string Code { get; init; } = string.Empty;

    public string DisplayNameEn { get; init; } = string.Empty;

    public string? DisplayNameBn { get; init; }

    public string? DescriptionEn { get; init; }

    public string? DescriptionBn { get; init; }

    public string? IconUrl { get; init; }

    public bool IsEnabled { get; init; }

    public int SortOrder { get; init; }

    public PaymentMode Mode { get; init; }

    public decimal? MinOrderAmount { get; init; }

    /// <summary>
    /// The cash-on-delivery ceiling, when this is cash on delivery.
    /// </summary>
    /// <remarks>
    /// Sending a rider out to collect 180,000৳ in notes is a risk the shop may
    /// not want to carry, and this is the one field where it says so.
    /// </remarks>
    public decimal? MaxOrderAmount { get; init; }

    public bool AvailableInsideDhaka { get; init; }

    public bool AvailableOutsideDhaka { get; init; }

    public PaymentChargeType ChargeType { get; init; }

    public decimal ChargeValue { get; init; }

    /// <summary>Whether a credential is stored. Never what it is.</summary>
    public bool HasCredentials { get; init; }

    /// <summary>
    /// A credential is stored but cannot be decrypted.
    /// </summary>
    /// <remarks>
    /// Nearly always a Data Protection key ring that was rotated or lost with a
    /// container, rather than tampering. It has to be visible here, because the
    /// alternative is discovering it at the moment a customer tries to pay.
    /// </remarks>
    public bool CredentialsUnreadable { get; init; }

    /// <summary>
    /// Whether any code implements this method.
    /// </summary>
    /// <remarks>
    /// False for a method configured ahead of the class that will serve it —
    /// bKash, until Phase 5 writes it. The resolver silently skips such a row,
    /// so without this the screen would show a method enabled and the checkout
    /// would never offer it, with nothing anywhere to say why.
    /// </remarks>
    public bool IsImplemented { get; init; }

    /// <summary>The customer leaves the site to pay and comes back.</summary>
    public bool SupportsRedirect { get; init; }

    /// <summary>Money can be sent back through the gateway itself.</summary>
    public bool SupportsRefund { get; init; }

    /// <summary>Cannot work without a merchant credential.</summary>
    public bool NeedsCredentials { get; init; }
}

/// <summary>
/// Changing one method.
/// </summary>
/// <remarks>
/// The code is not here. It is the join to the provider that implements the
/// method, and editing it would turn a working row into one the resolver skips
/// — which looks exactly like nothing happening.
/// </remarks>
public class UpdatePaymentMethodDto
{
    [Required]
    [StringLength(80, MinimumLength = 2)]
    public string DisplayNameEn { get; init; } = string.Empty;

    [StringLength(80)]
    public string? DisplayNameBn { get; init; }

    [StringLength(500)]
    public string? DescriptionEn { get; init; }

    [StringLength(500)]
    public string? DescriptionBn { get; init; }

    [StringLength(500)]
    public string? IconUrl { get; init; }

    public bool IsEnabled { get; init; }

    [Range(0, 9999)]
    public int SortOrder { get; init; }

    public PaymentMode Mode { get; init; }

    [Range(0, 99_999_999)]
    public decimal? MinOrderAmount { get; init; }

    [Range(0, 99_999_999)]
    public decimal? MaxOrderAmount { get; init; }

    public bool AvailableInsideDhaka { get; init; }

    public bool AvailableOutsideDhaka { get; init; }

    public PaymentChargeType ChargeType { get; init; }

    [Range(0, 99_999_999)]
    public decimal ChargeValue { get; init; }

    /// <summary>
    /// A new credential blob, or nothing.
    /// </summary>
    /// <remarks>
    /// <b>Three states, and they are all different.</b> Absent leaves what is
    /// stored alone — which is what every save that is not about credentials
    /// sends, because the screen has nothing to send back. An empty string
    /// clears it. Anything else replaces it. Without the distinction, opening
    /// the form and pressing save would wipe a merchant key.
    /// </remarks>
    [StringLength(2000)]
    public string? Credentials { get; init; }
}

/// <summary>Limits shared between the attributes and the service.</summary>
public static class PaymentMethodRules
{
    /// <summary>A percentage charge above this is a typo, not a policy.</summary>
    public const decimal MaxPercentCharge = 100m;
}
