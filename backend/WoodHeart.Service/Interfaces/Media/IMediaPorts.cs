using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Repository;

namespace WoodHeart.Service.Interfaces.Media;

/// <summary>
/// Where binary media lives. Implemented by Cloudinary.
/// </summary>
/// <remarks>
/// <para>
/// A port, in the same sense as <c>ISmsSender</c>: the catalog services talk to
/// this and never to a vendor SDK, so a product's media rules are testable
/// without an account and without a network.
/// </para>
/// <para>
/// <b>Two upload paths, deliberately, because images and video are not the same
/// problem.</b>
/// </para>
/// <para>
/// An image is a few megabytes and benefits from being checked before it
/// reaches anyone else — extension, magic bytes, size — so it goes through this
/// server, which is the only place that check can be trusted.
/// </para>
/// <para>
/// A video is hundreds of megabytes. Streaming that through the API costs a
/// request slot for minutes, a raised request-size limit, and a timeout window
/// wide enough to be worth abusing — all to add nothing, since we would forward
/// it unmodified. So video goes straight from the browser to Cloudinary under a
/// short-lived signed ticket, and this server verifies afterwards that what
/// landed is what it authorised. See <see cref="CreateVideoUploadTicketAsync"/>
/// and <see cref="GetAsync"/>.
/// </para>
/// </remarks>
public interface IMediaStorage
{
    /// <summary>
    /// False when no credentials are configured — CI, and a first checkout.
    /// </summary>
    /// <remarks>
    /// Checked rather than assumed, so the API still starts and the storefront
    /// still serves. Only uploading fails, and it fails with a message that
    /// says why instead of a null reference from inside the SDK.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>Uploads image bytes and returns what Cloudinary stored.</summary>
    Task<GeneralResponse<StoredMedia>> UploadImageAsync(
        MediaUpload upload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs a short-lived ticket letting the browser upload one video directly.
    /// </summary>
    /// <remarks>
    /// The public id is chosen here, not by the caller. Cloudinary's signature
    /// has to cover every parameter the client sends, so a client that adds or
    /// changes one is rejected — which is what stops a signed ticket from
    /// becoming permission to overwrite an arbitrary asset.
    /// </remarks>
    Task<GeneralResponse<VideoUploadTicket>> CreateVideoUploadTicketAsync(
        string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an asset's real metadata back from Cloudinary. Null when absent.
    /// </summary>
    /// <remarks>
    /// This is the verification step after a direct upload, and it is not
    /// optional. The browser reports which public id it created; believing that
    /// report would let a caller attach any asset in the account to any
    /// product, and would let it claim any dimensions it liked. Width, height,
    /// byte count and format all come from here.
    /// </remarks>
    Task<GeneralResponse<StoredMedia?>> GetAsync(
        string publicId, MediaType mediaType, CancellationToken cancellationToken = default);

    /// <summary>Permanently destroys an asset. Succeeds if it is already gone.</summary>
    Task<GeneralResponse> DeleteAsync(
        string publicId, MediaType mediaType, CancellationToken cancellationToken = default);
}

/// <summary>Image bytes on their way to storage.</summary>
public record MediaUpload
{
    public required Stream Content { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long Length { get; init; }

    /// <summary>Folder beneath the configured root, e.g. <c>products/7</c>.</summary>
    public required string Folder { get; init; }
}

/// <summary>What storage actually holds, as storage reports it.</summary>
/// <remarks>
/// Every field is Cloudinary's answer rather than the caller's claim, including
/// on the direct-upload path. <see cref="PublicId"/> is what goes in
/// <c>ProductMedia.StoragePath</c> — a key, never a URL.
/// </remarks>
public record StoredMedia
{
    public required string PublicId { get; init; }

    public required MediaType MediaType { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public long? Bytes { get; init; }

    /// <summary>e.g. <c>jpg</c>, <c>mp4</c>. Recorded as a MIME type on the row.</summary>
    public string? Format { get; init; }
}

/// <summary>
/// Everything the browser needs to POST one video to Cloudinary itself.
/// </summary>
/// <remarks>
/// Carries the api key, which is public — the secret stays here and is only
/// ever used to produce <see cref="Signature"/>.
/// </remarks>
public record VideoUploadTicket
{
    public required string UploadUrl { get; init; }

    public required string ApiKey { get; init; }

    public required long Timestamp { get; init; }

    public required string Signature { get; init; }

    public required string PublicId { get; init; }

    public required string Folder { get; init; }
}
