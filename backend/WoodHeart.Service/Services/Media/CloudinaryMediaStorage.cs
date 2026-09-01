using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;
using WoodHeart.Service.Interfaces.Media;

namespace WoodHeart.Service.Services.Media;

/// <summary>
/// <see cref="IMediaStorage"/> over Cloudinary.
/// </summary>
/// <remarks>
/// <para>
/// The only class in the solution that references the Cloudinary SDK. Everything
/// above it works in terms of a public id and a <see cref="StoredMedia"/>.
/// </para>
/// <para>
/// <b>Nothing here composes a delivery URL.</b> The storefront builds those from
/// the public id, because the transformation depends on where the image is
/// being rendered — a card wants 400px, a product page wants 1200, and a
/// <c>srcset</c> wants five widths. Returning fixed URLs from here would mean
/// either shipping every size on every row or serving one size everywhere.
/// </para>
/// </remarks>
public class CloudinaryMediaStorage : IMediaStorage
{
    private readonly CloudinarySettings settings;
    private readonly IDateTimeProvider clock;
    private readonly ILogger<CloudinaryMediaStorage> logger;
    private readonly Cloudinary? cloudinary;

    public CloudinaryMediaStorage(
        IOptions<CloudinarySettings> options,
        IDateTimeProvider clock,
        ILogger<CloudinaryMediaStorage> logger)
    {
        settings = options.Value;
        this.clock = clock;
        this.logger = logger;

        // Constructed once when configured, and left null when not. The
        // alternative — building it lazily and letting the SDK throw on a blank
        // cloud name — turns a missing environment variable into a stack trace
        // from inside a vendor library.
        if (settings.IsConfigured)
        {
            cloudinary = new Cloudinary(
                new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret));
        }
        else
        {
            MediaLog.NotConfigured(logger);
        }
    }

    public bool IsConfigured => cloudinary is not null;

    public async Task<GeneralResponse<StoredMedia>> UploadImageAsync(
        MediaUpload upload, CancellationToken cancellationToken = default)
    {
        if (cloudinary is null)
        {
            return GeneralResponse<StoredMedia>.Fail(
                CatalogErrors.MediaStorageUnavailable,
                "Media storage is not configured on this server.");
        }

        var parameters = new ImageUploadParams
        {
            File = new FileDescription(upload.FileName, upload.Content),
            Folder = Scoped(upload.Folder),

            // The uploaded file name never becomes the public id. It is caller
            // input, it collides, and it leaks whatever the photographer's
            // export settings called the file into a public URL.
            UseFilename = false,
            UniqueFilename = true,

            // Never overwrite. Every upload is a new asset, so a public id
            // already recorded against a row can never start pointing at
            // different bytes — which is also why the delivery URLs need no
            // version and no cache busting.
            Overwrite = false
        };

        try
        {
            var result = await cloudinary.UploadAsync(parameters, cancellationToken);

            if (result.Error is not null)
            {
                MediaLog.UploadRejected(logger, result.Error.Message);

                return GeneralResponse<StoredMedia>.Fail(
                    CatalogErrors.MediaUploadFailed, "The image could not be stored.");
            }

            return GeneralResponse<StoredMedia>.Success(new StoredMedia
            {
                PublicId = result.PublicId,
                MediaType = MediaType.Image,
                Width = result.Width,
                Height = result.Height,
                Bytes = result.Bytes,
                Format = result.Format
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MediaLog.StorageThrew(logger, exception, "upload", upload.FileName);

            return GeneralResponse<StoredMedia>.Fail(
                CatalogErrors.MediaUploadFailed, "The image could not be stored.");
        }
    }

    public Task<GeneralResponse<VideoUploadTicket>> CreateVideoUploadTicketAsync(
        string folder, CancellationToken cancellationToken = default)
    {
        if (cloudinary is null)
        {
            return Task.FromResult(GeneralResponse<VideoUploadTicket>.Fail(
                CatalogErrors.MediaStorageUnavailable,
                "Media storage is not configured on this server."));
        }

        var scopedFolder = Scoped(folder);

        // Chosen here, not by the browser. A ticket that let the client name the
        // asset would be permission to write anywhere in the account.
        var publicId = $"{scopedFolder}/{Guid.NewGuid():n}";

        var timestamp = clock.UtcNow.ToUnixTimeSeconds();

        // Cloudinary validates the signature against *every* parameter the
        // client sends, so this set is a whitelist as much as a signature: add
        // a parameter at the browser and the upload is rejected.
        var toSign = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["folder"] = scopedFolder,
            ["public_id"] = publicId,
            ["timestamp"] = timestamp
        };

        return Task.FromResult(GeneralResponse<VideoUploadTicket>.Success(new VideoUploadTicket
        {
            UploadUrl = $"https://api.cloudinary.com/v1_1/{settings.CloudName}/video/upload",
            ApiKey = settings.ApiKey,
            Timestamp = timestamp,
            Signature = cloudinary.Api.SignParameters(toSign),
            PublicId = publicId,
            Folder = scopedFolder
        }));
    }

    public async Task<GeneralResponse<StoredMedia?>> GetAsync(
        string publicId, MediaType mediaType, CancellationToken cancellationToken = default)
    {
        if (cloudinary is null)
        {
            return GeneralResponse<StoredMedia?>.Fail(
                CatalogErrors.MediaStorageUnavailable,
                "Media storage is not configured on this server.");
        }

        try
        {
            var result = await cloudinary.GetResourceAsync(
                new GetResourceParams(publicId) { ResourceType = ResourceTypeFor(mediaType) },
                cancellationToken);

            // A public id that does not exist is a normal answer here, not a
            // fault: it is what a client claiming an upload it never made looks
            // like. The caller turns it into a refusal.
            if (result.StatusCode == System.Net.HttpStatusCode.NotFound || result.Error is not null)
            {
                return GeneralResponse<StoredMedia?>.Success(null);
            }

            return GeneralResponse<StoredMedia?>.Success(new StoredMedia
            {
                PublicId = result.PublicId,
                MediaType = mediaType,
                Width = result.Width,
                Height = result.Height,
                Bytes = result.Bytes,
                Format = result.Format
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MediaLog.StorageThrew(logger, exception, "read", publicId);

            return GeneralResponse<StoredMedia?>.Fail(
                CatalogErrors.MediaStorageUnavailable, "Media storage could not be reached.");
        }
    }

    public async Task<GeneralResponse> DeleteAsync(
        string publicId, MediaType mediaType, CancellationToken cancellationToken = default)
    {
        if (cloudinary is null)
        {
            return GeneralResponse.Fail(
                CatalogErrors.MediaStorageUnavailable,
                "Media storage is not configured on this server.");
        }

        try
        {
            var result = await cloudinary.DestroyAsync(new DeletionParams(publicId)
            {
                ResourceType = ResourceTypeFor(mediaType)
            });

            // "not found" is the desired end state, so it is a success. Delete
            // has to be safe to retry — the caller runs it after the row is
            // already gone.
            if (result.Error is not null
                && result.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                MediaLog.DestroyRefused(logger, publicId, result.Error.Message);

                return GeneralResponse.Fail(
                    CatalogErrors.MediaStorageUnavailable, "The asset could not be removed.");
            }

            return GeneralResponse.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MediaLog.StorageThrew(logger, exception, "destroy", publicId);

            return GeneralResponse.Fail(
                CatalogErrors.MediaStorageUnavailable, "The asset could not be removed.");
        }
    }

    // -------------------------------------------------------------------------

    /// <summary>Puts every path under the configured root folder.</summary>
    /// <remarks>
    /// The root is per environment, which is what keeps a staging clean-up from
    /// destroying live product photography in the same Cloudinary account.
    /// </remarks>
    private string Scoped(string folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? settings.Folder
            : $"{settings.Folder.TrimEnd('/')}/{folder.Trim('/')}";

    private static ResourceType ResourceTypeFor(MediaType mediaType) => mediaType switch
    {
        MediaType.Video => ResourceType.Video,
        // A PDF assembly guide is "image" to Cloudinary — it rasterises them,
        // which is what makes a thumbnail of page one possible.
        MediaType.Document => ResourceType.Image,
        _ => ResourceType.Image
    };
}
