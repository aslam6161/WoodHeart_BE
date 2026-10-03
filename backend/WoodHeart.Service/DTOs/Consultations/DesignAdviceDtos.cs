using System.ComponentModel.DataAnnotations;
using WoodHeart.Service.DTOs.Catalog;

namespace WoodHeart.Service.DTOs.Consultations;

/// <summary>What the customer asked the design assistant.</summary>
public class DesignAdviceRequestDto
{
    /// <summary>
    /// The question, in English or Bangla.
    /// </summary>
    /// <remarks>
    /// Bounded at both ends. Under three characters is not a question; the
    /// ceiling is there because this text is forwarded to a metered service,
    /// and an unbounded field is an unbounded bill.
    /// </remarks>
    [Required]
    [StringLength(600, MinimumLength = 3)]
    public string Question { get; set; } = string.Empty;
}

/// <summary>
/// The assistant's answer, and the real products behind it.
/// </summary>
/// <remarks>
/// <b>The prose and the products are separate on purpose.</b> The model
/// chooses which pieces to point at; it never states their prices. Each piece
/// comes back as the same <see cref="StorefrontProductDto"/> the rest of the
/// storefront renders, read from the database after the model has answered —
/// so what a customer sees is the shop's own price, stock and link, and a model
/// that misremembers a figure cannot quote it to them.
/// </remarks>
public class DesignAdviceDto
{
    public string Reply { get; set; } = string.Empty;

    public IReadOnlyList<StorefrontProductDto> Products { get; set; } = [];
}
