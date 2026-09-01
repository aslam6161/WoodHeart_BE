using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Catalog;
using WoodHeart.Service.Interfaces.Media;
using WoodHeart.Service.Mapping.Catalog;
using WoodHeart.Service.Services.Media;

namespace WoodHeart.Service.Services.Catalog;

/// <summary>
/// Product photography and video: what is attached, in what order, and which
/// one is the hero.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two invariants are maintained here rather than left to whoever is using
/// the admin panel</b>, because both are invisible until a customer sees the
/// result:
/// </para>
/// <para>
/// A product with media always has exactly one primary. The first image
/// uploaded becomes primary whatever the caller asked for, and deleting the
/// primary promotes the next one. Without that, a product can hold eight
/// photographs and still show a blank card in every listing.
/// </para>
/// <para>
/// The database enforces the "exactly one" half with a filtered unique index,
/// which is why the swaps below are two saves inside one transaction rather
/// than one save. Within a single <c>SaveChanges</c> EF is free to order the
/// two <c>UPDATE</c>s either way, and the order where the new primary is set
/// first violates the index. That failure needs a real Postgres to reproduce —
/// a substituted repository will never show it.
/// </para>
/// </remarks>
public class ProductMediaService(
    IProductRepository products,
    IProductVariantRepository variants,
    IProductMediaRepository media,
    IMediaStorage storage,
    IUnitOfWork unitOfWork,
    IOptions<CloudinarySettings> cloudinaryOptions,
    ILogger<ProductMediaService> logger) : IProductMediaService
{
    private readonly CloudinarySettings settings = cloudinaryOptions.Value;

    public async Task<GeneralResponse<IReadOnlyList<ProductMediaDto>>> GetAsync(
        long productId, CancellationToken cancellationToken = default)
    {
        if (!await products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            return GeneralResponse<IReadOnlyList<ProductMediaDto>>.Fail(
                CatalogErrors.ProductNotFound, $"No product with the id {productId}.");
        }

        var rows = await media.GetByProductAsync(productId, cancellationToken);

        return GeneralResponse<IReadOnlyList<ProductMediaDto>>.Success(
            [.. rows.Select(CatalogMapper.ToDto)]);
    }

    public async Task<GeneralResponse<ProductMediaDto>> UploadImageAsync(
        long productId, UploadProductImageDto dto, CancellationToken cancellationToken = default)
    {
        var guard = await ValidateTargetAsync(productId, dto.VariantId, cancellationToken);

        if (guard is not null)
        {
            return GeneralResponse<ProductMediaDto>.Fail(guard.ErrorCode!, guard.Message);
        }

        if (dto.Length > settings.MaxImageBytes)
        {
            return GeneralResponse<ProductMediaDto>.Fail(
                CatalogErrors.MediaTooLarge,
                $"That image is {dto.Length / (1024 * 1024)} MB. The limit is "
                + $"{settings.MaxImageBytes / (1024 * 1024)} MB.");
        }

        var contentType = await SniffAsync(dto.Content, cancellationToken);

        if (contentType is null)
        {
            return GeneralResponse<ProductMediaDto>.Fail(
                CatalogErrors.MediaUnsupportedFormat,
                "That file is not a JPEG, PNG, WebP, AVIF or HEIC image.");
        }

        // The upload runs before the transaction opens. Holding a database
        // transaction across a multi-megabyte HTTP upload would tie up a
        // connection for as long as the slowest client takes.
        var stored = await storage.UploadImageAsync(
            new MediaUpload
            {
                Content = dto.Content,
                FileName = dto.FileName,
                ContentType = contentType,
                Length = dto.Length,
                Folder = FolderFor(productId)
            },
            cancellationToken);

        if (!stored.IsSuccess || stored.Data is null)
        {
            return GeneralResponse<ProductMediaDto>.Fail(stored.ErrorCode!, stored.Message);
        }

        try
        {
            var row = await AttachAsync(
                productId,
                stored.Data,
                dto.VariantId,
                dto.AltText,
                dto.Caption,
                dto.IsPrimary,
                contentType,
                cancellationToken);

            return GeneralResponse<ProductMediaDto>.Success(CatalogMapper.ToDto(row), id: row.Id);
        }
        catch
        {
            // The bytes are in Cloudinary and nothing in our database points at
            // them. Compensate rather than leak: an asset no row references is
            // billed storage that nobody will ever find again.
            await storage.DeleteAsync(stored.Data.PublicId, MediaType.Image, CancellationToken.None);
            throw;
        }
    }

    public async Task<GeneralResponse<VideoUploadTicketDto>> CreateVideoTicketAsync(
        long productId, CancellationToken cancellationToken = default)
    {
        if (!await products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            return GeneralResponse<VideoUploadTicketDto>.Fail(
                CatalogErrors.ProductNotFound, $"No product with the id {productId}.");
        }

        var ticket = await storage.CreateVideoUploadTicketAsync(
            FolderFor(productId), cancellationToken);

        if (!ticket.IsSuccess || ticket.Data is null)
        {
            return GeneralResponse<VideoUploadTicketDto>.Fail(ticket.ErrorCode!, ticket.Message);
        }

        return GeneralResponse<VideoUploadTicketDto>.Success(new VideoUploadTicketDto
        {
            UploadUrl = ticket.Data.UploadUrl,
            ApiKey = ticket.Data.ApiKey,
            Timestamp = ticket.Data.Timestamp,
            Signature = ticket.Data.Signature,
            PublicId = ticket.Data.PublicId,
            Folder = ticket.Data.Folder
        });
    }

    public async Task<GeneralResponse<ProductMediaDto>> ConfirmVideoAsync(
        long productId, ConfirmVideoUploadDto dto, CancellationToken cancellationToken = default)
    {
        var guard = await ValidateTargetAsync(productId, dto.VariantId, cancellationToken);

        if (guard is not null)
        {
            return GeneralResponse<ProductMediaDto>.Fail(guard.ErrorCode!, guard.Message);
        }

        // Check the shape of the id before spending a network call on it, and —
        // more to the point — before letting a caller attach an asset from
        // somewhere else in the Cloudinary account. Tickets are only ever issued
        // for this prefix.
        var expectedPrefix = $"{settings.Folder.TrimEnd('/')}/{FolderFor(productId)}/";

        if (!dto.PublicId.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return GeneralResponse<ProductMediaDto>.Fail(
                CatalogErrors.MediaNotUploaded,
                "That asset was not uploaded for this product.");
        }

        var lookup = await storage.GetAsync(dto.PublicId, MediaType.Video, cancellationToken);

        if (!lookup.IsSuccess)
        {
            return GeneralResponse<ProductMediaDto>.Fail(lookup.ErrorCode!, lookup.Message);
        }

        if (lookup.Data is null)
        {
            return GeneralResponse<ProductMediaDto>.Fail(
                CatalogErrors.MediaNotUploaded,
                "No such video has been uploaded. Upload it before confirming.");
        }

        var row = await AttachAsync(
            productId,
            lookup.Data,
            dto.VariantId,
            dto.AltText,
            dto.Caption,
            // A video is never the hero image. A card with a video where the
            // photograph should be is a card with nothing on it.
            makePrimary: false,
            contentType: lookup.Data.Format is { } format ? $"video/{format}" : null,
            cancellationToken);

        return GeneralResponse<ProductMediaDto>.Success(CatalogMapper.ToDto(row), id: row.Id);
    }

    public async Task<GeneralResponse<ProductMediaDto>> UpdateAsync(
        long productId,
        long mediaId,
        UpdateProductMediaDto dto,
        CancellationToken cancellationToken = default)
    {
        var row = await media.GetForProductAsync(productId, mediaId, cancellationToken);

        if (row is null)
        {
            return GeneralResponse<ProductMediaDto>.Fail(
                CatalogErrors.MediaNotFound, $"No media with the id {mediaId} on this product.");
        }

        row.AltText = dto.AltText;
        row.Caption = dto.Caption;

        media.Update(row);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<ProductMediaDto>.Success(CatalogMapper.ToDto(row));
    }

    public async Task<GeneralResponse<ProductMediaDto>> SetPrimaryAsync(
        long productId, long mediaId, CancellationToken cancellationToken = default)
    {
        var row = await media.GetForProductAsync(productId, mediaId, cancellationToken);

        if (row is null)
        {
            return GeneralResponse<ProductMediaDto>.Fail(
                CatalogErrors.MediaNotFound, $"No media with the id {mediaId} on this product.");
        }

        if (row.MediaType != MediaType.Image)
        {
            return GeneralResponse<ProductMediaDto>.Fail(
                CatalogErrors.MediaUnsupportedFormat,
                "Only an image can be the primary media for a product.");
        }

        if (row.IsPrimary)
        {
            return GeneralResponse<ProductMediaDto>.Success(CatalogMapper.ToDto(row));
        }

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await ClearPrimaryAsync(productId, exceptMediaId: mediaId, ct);

            row.IsPrimary = true;
            media.Update(row);
            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return GeneralResponse<ProductMediaDto>.Success(CatalogMapper.ToDto(row));
    }

    public async Task<GeneralResponse<IReadOnlyList<ProductMediaDto>>> ReorderAsync(
        long productId, ReorderProductMediaDto dto, CancellationToken cancellationToken = default)
    {
        var rows = await media.GetTrackedByProductAsync(productId, cancellationToken);

        if (rows.Count == 0)
        {
            return GeneralResponse<IReadOnlyList<ProductMediaDto>>.Fail(
                CatalogErrors.MediaNotFound, "This product has no media to reorder.");
        }

        var byId = rows.ToDictionary(m => m.Id);

        // Every id must belong to this product. A list containing one id from
        // somewhere else would otherwise silently reorder half the gallery and
        // ignore the rest.
        if (dto.MediaIds.Any(id => !byId.ContainsKey(id)))
        {
            return GeneralResponse<IReadOnlyList<ProductMediaDto>>.Fail(
                CatalogErrors.MediaNotFound,
                "The list contains media that does not belong to this product.");
        }

        var position = 0;

        foreach (var id in dto.MediaIds)
        {
            byId[id].SortOrder = position++;
        }

        // Anything the caller left out keeps a stable place at the end rather
        // than colliding at sort order zero. A partial list is a client bug, not
        // a reason to scramble the gallery.
        foreach (var row in rows.Where(m => !dto.MediaIds.Contains(m.Id)).OrderBy(m => m.SortOrder))
        {
            row.SortOrder = position++;
        }

        media.UpdateRange(rows);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<IReadOnlyList<ProductMediaDto>>.Success(
            [.. rows.OrderBy(m => m.SortOrder).Select(CatalogMapper.ToDto)]);
    }

    public async Task<GeneralResponse> DeleteAsync(
        long productId, long mediaId, CancellationToken cancellationToken = default)
    {
        var row = await media.GetForProductAsync(productId, mediaId, cancellationToken);

        if (row is null)
        {
            return GeneralResponse.Fail(
                CatalogErrors.MediaNotFound, $"No media with the id {mediaId} on this product.");
        }

        var publicId = row.StoragePath;
        var mediaType = row.MediaType;
        var wasPrimary = row.IsPrimary;

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // Two saves, in this order, for the same reason as the swap above:
            // the filtered unique index counts a row as primary until it is
            // actually marked deleted, so promoting the successor first would
            // briefly leave two.
            row.IsPrimary = false;
            media.Delete(row);
            await unitOfWork.SaveChangesAsync(ct);

            if (wasPrimary)
            {
                var successor = await media.GetPrimaryCandidateAsync(productId, mediaId, ct);

                if (successor is not null)
                {
                    successor.IsPrimary = true;
                    media.Update(successor);
                    await unitOfWork.SaveChangesAsync(ct);
                }
            }
        }, cancellationToken);

        // After the commit, and best-effort on purpose. The row is already gone
        // from every page, so a failure here costs a little storage; failing the
        // request instead would report an error for work that succeeded, and
        // destroying the asset first would risk a live row pointing at bytes
        // that no longer exist — a broken image on a product page, which is
        // strictly worse than an orphan.
        var destroyed = await storage.DeleteAsync(publicId, mediaType, CancellationToken.None);

        if (!destroyed.IsSuccess)
        {
            MediaLog.AssetOrphaned(logger, mediaId, publicId);
        }

        return GeneralResponse.Success("Media removed.", mediaId);
    }

    // -------------------------------------------------------------------------

    /// <summary>Product exists, and the variant — if named — belongs to it.</summary>
    private async Task<GeneralResponse?> ValidateTargetAsync(
        long productId, long? variantId, CancellationToken cancellationToken)
    {
        if (!await products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            return GeneralResponse.Fail(
                CatalogErrors.ProductNotFound, $"No product with the id {productId}.");
        }

        if (variantId is not { } id)
        {
            return null;
        }

        // Checked against this product, not merely for existence. Attaching a
        // photograph to another product's variant would make it appear on a
        // page it has nothing to do with.
        if (!await variants.AnyAsync(v => v.Id == id && v.ProductId == productId, cancellationToken))
        {
            return GeneralResponse.Fail(
                CatalogErrors.MediaVariantMismatch,
                "That variant does not belong to this product.");
        }

        return null;
    }

    /// <summary>Writes the row for something already in storage.</summary>
    private async Task<ProductMedia> AttachAsync(
        long productId,
        StoredMedia stored,
        long? variantId,
        string? altText,
        string? caption,
        bool makePrimary,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var existingPrimary = await media.GetPrimaryAsync(productId, cancellationToken);

        // The first image is primary whether or not anyone asked. A product
        // with photographs and no hero renders a blank card everywhere it
        // appears, and nothing reports it.
        var isPrimary = stored.MediaType == MediaType.Image
                        && (makePrimary || existingPrimary is null);

        var row = new ProductMedia
        {
            ProductId = productId,
            VariantId = variantId,
            MediaType = stored.MediaType,
            StoragePath = stored.PublicId,
            AltText = altText,
            Caption = caption,
            IsPrimary = isPrimary,
            SortOrder = await media.MaxSortOrderAsync(productId, cancellationToken) + 1,
            Width = stored.Width,
            Height = stored.Height,
            FileSizeBytes = stored.Bytes,
            ContentType = contentType
        };

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (isPrimary && existingPrimary is not null)
            {
                await ClearPrimaryAsync(productId, exceptMediaId: null, ct);
            }

            await media.InsertAsync(row, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return row;
    }

    /// <summary>
    /// Unsets the current primary <b>and commits</b>, so the index only ever
    /// sees one primary at a time.
    /// </summary>
    /// <remarks>
    /// The commit is the point. Staging both changes for a single
    /// <c>SaveChanges</c> lets EF emit the two <c>UPDATE</c>s in either order,
    /// and one of those orders leaves two rows matching
    /// <c>ux_product_media_one_primary</c> — a constraint violation that
    /// appears only against a real Postgres, and only sometimes.
    /// </remarks>
    private async Task ClearPrimaryAsync(
        long productId, long? exceptMediaId, CancellationToken cancellationToken)
    {
        var current = await media.GetPrimaryAsync(productId, cancellationToken);

        if (current is null || current.Id == exceptMediaId)
        {
            return;
        }

        current.IsPrimary = false;
        media.Update(current);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Where a product's assets live, beneath the configured root.</summary>
    private static string FolderFor(long productId) => $"products/{productId}";

    /// <summary>
    /// Reads the first bytes and returns the real content type, or null.
    /// </summary>
    /// <remarks>
    /// Rewinds afterwards, because the same stream is then handed to the
    /// uploader. Forgetting that is how a "successful" upload produces a file
    /// missing its first sixteen bytes.
    /// </remarks>
    private static async Task<string?> SniffAsync(Stream content, CancellationToken cancellationToken)
    {
        var header = new byte[ImageFileInspector.HeaderBytes];
        var read = await content.ReadAtLeastAsync(
            header, header.Length, throwOnEndOfStream: false, cancellationToken);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        return ImageFileInspector.DetectContentType(header.AsSpan(0, read));
    }
}
