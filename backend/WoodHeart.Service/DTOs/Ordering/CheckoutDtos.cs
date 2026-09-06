using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Service.DTOs.Ordering;

/// <summary>Where the order is going, as the checkout form collects it.</summary>
public class DeliveryAddressDto
{
    [Required]
    [MaxLength(DeliveryAddress.MaxDivision)]
    public string Division { get; init; } = string.Empty;

    [Required]
    [MaxLength(DeliveryAddress.MaxDistrict)]
    public string District { get; init; } = string.Empty;

    [MaxLength(DeliveryAddress.MaxUpazila)]
    public string? Upazila { get; init; }

    [MaxLength(DeliveryAddress.MaxArea)]
    public string? Area { get; init; }

    [Required]
    [MaxLength(DeliveryAddress.MaxAddressLine)]
    public string AddressLine { get; init; } = string.Empty;

    /// <summary>How the rider actually finds it. Optional, and worth asking for.</summary>
    [MaxLength(DeliveryAddress.MaxLandmark)]
    public string? Landmark { get; init; }

    [MaxLength(DeliveryAddress.MaxPostcode)]
    public string? Postcode { get; init; }
}

/// <summary>
/// Everything needed to turn the caller's basket into an order.
/// </summary>
/// <remarks>
/// <b>Note what is absent: any money at all.</b> No subtotal, no delivery fee,
/// no grand total. Every figure is recomputed server-side from the caller's own
/// cart at the moment of placement, because a client that could name a price is
/// a client that can name zero. What the customer saw is re-derived, not
/// received.
/// </remarks>
public class PlaceOrderDto
{
    [Required]
    [MaxLength(120)]
    public string ContactName { get; init; } = string.Empty;

    /// <summary>Any of the four ways Bangladeshis write a number. Normalised here.</summary>
    [Required]
    [MaxLength(20)]
    public string ContactPhone { get; init; } = string.Empty;

    [EmailAddress]
    [MaxLength(256)]
    public string? ContactEmail { get; init; }

    [Required]
    public DeliveryAddressDto ShippingAddress { get; init; } = new();

    /// <summary><c>cod</c>, or whatever else the shop has enabled.</summary>
    [Required]
    [MaxLength(32)]
    public string PaymentMethodCode { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? DeliveryNote { get; init; }

    /// <summary>
    /// Where the gateway should send the customer back to, for methods that
    /// redirect. Ignored by cash on delivery.
    /// </summary>
    [MaxLength(500)]
    public string? ReturnUrl { get; init; }
}

/// <summary>One payment method the customer may choose, as checkout renders it.</summary>
public class PaymentMethodDto
{
    public string Code { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? IconUrl { get; init; }

    /// <summary>What choosing this adds to the bill. Zero for most.</summary>
    /// <remarks>
    /// Shown before the choice is made, not after. A charge that appears only
    /// on the confirmation is the one customers write reviews about.
    /// </remarks>
    public decimal Surcharge { get; init; }

    /// <summary>True when picking this sends the customer to a gateway and back.</summary>
    public bool RedirectsToGateway { get; init; }
}

/// <summary>What came back from placing an order.</summary>
public class PlacedOrderDto
{
    public long Id { get; init; }

    /// <summary>What the customer quotes: <c>WH-2609-00042</c>.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    public OrderStatus Status { get; init; }

    public decimal GrandTotal { get; init; }

    /// <summary>
    /// Set only when the payment method sends the customer to a gateway.
    /// </summary>
    /// <remarks>
    /// Null for cash on delivery, which is the whole point of the flag: the
    /// storefront shows a confirmation page rather than looking for somewhere
    /// to redirect to.
    /// </remarks>
    public string? RedirectUrl { get; init; }

    /// <summary>
    /// True when this response is a repeat of a placement that already
    /// happened.
    /// </summary>
    /// <remarks>
    /// A double-tap gets the first order back with this set, rather than a
    /// second order or an error. The storefront can carry on to the
    /// confirmation page exactly as it would have.
    /// </remarks>
    public bool AlreadyPlaced { get; init; }
}
