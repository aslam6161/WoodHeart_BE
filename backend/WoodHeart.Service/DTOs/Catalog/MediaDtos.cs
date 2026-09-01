using System.ComponentModel.DataAnnotations;

namespace WoodHeart.Service.DTOs.Catalog;

/// <summary>
/// One image on its way in, already separated from the HTTP request.
/// </summary>
/// <remarks>
/// Carries a <see cref="Stream"/> rather than an <c>IFormFile</c> so the
/// service layer does not have an opinion about HTTP. The controller opens the
/// stream and owns its lifetime; nothing here disposes it.
/// </remarks>
public class UploadProductImageDto
{
    public required Stream Content { get; init; }

    public required string FileName { get; init; }

    public required long Length { get; init; }

    /// <summary>Attach to one variant, so it only shows once that option is picked.</summary>
    public long? VariantId { get; init; }

    /// <summary>
    /// What a screen reader announces and what Google reads.
    /// </summary>
    /// <remarks>
    /// <b>Required, and that is a deliberate piece of friction.</b> Alt text is
    /// the classic field that is optional in the form and therefore empty on
    /// every row — and an empty one is a silent accessibility and SEO failure
    /// on the page that sells the product. Making it a validation error is the
    /// only version of this rule that survives a busy afternoon.
    /// </remarks>
    [Required(ErrorMessage = "Alt text is required. Describe the photograph in a few words.")]
    [StringLength(300, MinimumLength = 3)]
    public string AltText { get; init; } = string.Empty;

    /// <summary>Optional visible caption, distinct from alt text.</summary>
    [StringLength(500)]
    public string? Caption { get; init; }

    /// <summary>
    /// Make this the hero image.
    /// </summary>
    /// <remarks>
    /// Ignored when the product has no media yet — the first image is always
    /// primary, because a product with photographs and no hero shows a blank
    /// card in every listing.
    /// </remarks>
    public bool IsPrimary { get; init; }
}

/// <summary>
/// Records a video the browser uploaded straight to Cloudinary.
/// </summary>
/// <remarks>
/// The public id is checked against storage before anything is written. It
/// arrives from the client, and a client that can name an asset it did not
/// create could otherwise attach any video in the account to any product.
/// </remarks>
public class ConfirmVideoUploadDto
{
    [Required]
    [StringLength(512)]
    public string PublicId { get; init; } = string.Empty;

    public long? VariantId { get; init; }

    [StringLength(300)]
    public string? AltText { get; init; }

    [StringLength(500)]
    public string? Caption { get; init; }
}

/// <summary>Edits the text on an existing media row. The asset itself is immutable.</summary>
public class UpdateProductMediaDto
{
    [Required]
    [StringLength(300, MinimumLength = 3)]
    public string AltText { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Caption { get; init; }
}

/// <summary>
/// The gallery order, as a complete list rather than a move instruction.
/// </summary>
/// <remarks>
/// Every id, in the order they should appear. A "move item 3 to position 1"
/// API needs the client and the server to agree on what the current order was,
/// and they will not once two people are editing the same product. Sending the
/// whole list makes the last writer win, which is at least a rule you can
/// explain.
/// </remarks>
public class ReorderProductMediaDto
{
    [Required]
    [MinLength(1, ErrorMessage = "Send the media ids in the order they should appear.")]
    public IReadOnlyList<long> MediaIds { get; init; } = [];
}

/// <summary>
/// What the browser needs to upload one video directly to Cloudinary.
/// </summary>
/// <remarks>
/// The API key here is the public half of the credential pair. The secret never
/// leaves the server; it exists in this response only as the signature.
/// </remarks>
public class VideoUploadTicketDto
{
    public string UploadUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public long Timestamp { get; set; }

    public string Signature { get; set; } = string.Empty;

    public string PublicId { get; set; } = string.Empty;

    public string Folder { get; set; } = string.Empty;
}
