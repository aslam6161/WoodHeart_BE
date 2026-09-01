using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Catalog;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>Photography and video for one product.</summary>
/// <remarks>
/// <para>
/// Nested under the product on purpose: <c>/api/admin/products/{productId}/media/{mediaId}</c>.
/// The service looks every row up by <i>both</i> ids, so changing the media id
/// in the URL cannot reach another product's gallery.
/// </para>
/// <para>
/// <b>Images and video take different routes, and the asymmetry is the design.</b>
/// An image is small enough to be worth inspecting before it exists anywhere
/// public, so it goes through <see cref="UploadImage"/> and this server checks
/// its magic bytes. A video is hundreds of megabytes and would gain nothing
/// from the same trip — so <see cref="CreateVideoTicket"/> hands the browser a
/// signed ticket, the bytes go straight to Cloudinary, and
/// <see cref="ConfirmVideo"/> verifies with Cloudinary that what landed is what
/// was authorised.
/// </para>
/// </remarks>
[Authorize(Policy = Policies.RequireAdminOrManager)]
[Route("api/admin/products/{productId:long}/media")]
public class AdminProductMediaController(IProductMediaService media) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> Get(long productId, CancellationToken cancellationToken) =>
        HandleResult(await media.GetAsync(productId, cancellationToken));

    /// <summary>Uploads one image.</summary>
    /// <remarks>
    /// <para>
    /// The request limit is set here as well as in the service. This one stops
    /// the bytes at the edge — without it, a caller sending a gigabyte gets it
    /// buffered to disk in full before any code of ours reads the size and says
    /// no. The service's check is the one that produces a decent error message;
    /// this is the one that protects the server.
    /// </para>
    /// <para>
    /// The stream is opened here and closed by this method. The service reads
    /// it and never owns it.
    /// </para>
    /// </remarks>
    [HttpPost]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(
        long productId,
        [FromForm] UploadProductImageForm form,
        CancellationToken cancellationToken)
    {
        if (form.File is null || form.File.Length == 0)
        {
            return HandleResult(GeneralResponseFor.NoFile());
        }

        await using var content = form.File.OpenReadStream();

        var result = await media.UploadImageAsync(
            productId,
            new UploadProductImageDto
            {
                Content = content,
                FileName = form.File.FileName,
                Length = form.File.Length,
                VariantId = form.VariantId,
                AltText = form.AltText,
                Caption = form.Caption,
                IsPrimary = form.IsPrimary
            },
            cancellationToken);

        return HandleResult(result);
    }

    /// <summary>Signs a short-lived ticket for a direct video upload.</summary>
    [HttpPost("video-ticket")]
    public async Task<IActionResult> CreateVideoTicket(
        long productId, CancellationToken cancellationToken) =>
        HandleResult(await media.CreateVideoTicketAsync(productId, cancellationToken));

    /// <summary>Records a video the browser has already uploaded.</summary>
    [HttpPost("video")]
    public async Task<IActionResult> ConfirmVideo(
        long productId, ConfirmVideoUploadDto dto, CancellationToken cancellationToken) =>
        HandleResult(await media.ConfirmVideoAsync(productId, dto, cancellationToken));

    [HttpPut("{mediaId:long}")]
    public async Task<IActionResult> Update(
        long productId,
        long mediaId,
        UpdateProductMediaDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await media.UpdateAsync(productId, mediaId, dto, cancellationToken));

    /// <summary>Makes one image the hero.</summary>
    /// <remarks>
    /// Its own endpoint rather than a field on the update, because it changes
    /// another row as a side effect — the previous primary — and that is worth
    /// being explicit about in the route.
    /// </remarks>
    [HttpPost("{mediaId:long}/primary")]
    public async Task<IActionResult> SetPrimary(
        long productId, long mediaId, CancellationToken cancellationToken) =>
        HandleResult(await media.SetPrimaryAsync(productId, mediaId, cancellationToken));

    [HttpPost("order")]
    public async Task<IActionResult> Reorder(
        long productId, ReorderProductMediaDto dto, CancellationToken cancellationToken) =>
        HandleResult(await media.ReorderAsync(productId, dto, cancellationToken));

    [HttpDelete("{mediaId:long}")]
    public async Task<IActionResult> Delete(
        long productId, long mediaId, CancellationToken cancellationToken) =>
        HandleResult(await media.DeleteAsync(productId, mediaId, cancellationToken));
}

/// <summary>
/// The multipart form behind <see cref="AdminProductMediaController.UploadImage"/>.
/// </summary>
/// <remarks>
/// Lives here rather than in the service DTOs because <c>IFormFile</c> is an
/// HTTP concept. The service takes a stream and knows nothing about forms.
/// </remarks>
public class UploadProductImageForm
{
    public IFormFile? File { get; set; }

    public long? VariantId { get; set; }

    public string AltText { get; set; } = string.Empty;

    public string? Caption { get; set; }

    public bool IsPrimary { get; set; }
}

internal static class GeneralResponseFor
{
    public static WoodHeart.Repository.GeneralResponse<ProductMediaDto> NoFile() =>
        WoodHeart.Repository.GeneralResponse<ProductMediaDto>.Fail(
            CatalogErrors.MediaUnsupportedFormat, "No file was uploaded.");
}
