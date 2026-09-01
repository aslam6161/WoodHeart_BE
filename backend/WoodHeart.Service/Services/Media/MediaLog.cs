using Microsoft.Extensions.Logging;

namespace WoodHeart.Service.Services.Media;

/// <summary>
/// Source-generated logging for the media pipeline, matching
/// <c>StartupLog</c> and <c>ExceptionMiddleware</c>.
/// </summary>
/// <remarks>
/// <para>
/// Source-generated rather than interpolated for the reason CA1848 gives — no
/// boxing, no format parsing when the level is disabled — but mostly because
/// these are the messages someone reads at two in the morning when product
/// photographs have stopped appearing, and a stable event id is what makes
/// them findable.
/// </para>
/// <para>
/// <see cref="AssetOrphaned"/> is the one that matters. It is the only trace
/// left when a row is deleted and its Cloudinary asset is not.
/// </para>
/// </remarks>
internal static partial class MediaLog
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Warning,
        Message = "Cloudinary is not configured. Media uploads will be refused; the rest of the "
                  + "catalog is unaffected. Set Cloudinary:CloudName, :ApiKey and :ApiSecret.")]
    public static partial void NotConfigured(ILogger logger);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Error,
        Message = "Cloudinary rejected an upload: {Reason}")]
    public static partial void UploadRejected(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Error,
        Message = "Talking to Cloudinary threw during {Operation} of {PublicId}.")]
    public static partial void StorageThrew(
        ILogger logger, Exception exception, string operation, string publicId);

    [LoggerMessage(
        EventId = 1203,
        Level = LogLevel.Error,
        Message = "Cloudinary refused to destroy {PublicId}: {Reason}")]
    public static partial void DestroyRefused(ILogger logger, string publicId, string reason);

    [LoggerMessage(
        EventId = 1204,
        Level = LogLevel.Error,
        Message = "Media row {MediaId} was deleted but its asset {PublicId} still exists in "
                  + "Cloudinary. It is now unreferenced and must be removed by hand.")]
    public static partial void AssetOrphaned(ILogger logger, long mediaId, string publicId);
}
