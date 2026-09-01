namespace WoodHeart.Service.Services.Media;

/// <summary>
/// Decides whether a stream really is an image we accept, by reading it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The file name is not evidence.</b> A caller controls it completely, so
/// <c>.jpg</c> means only that someone typed <c>.jpg</c>. This reads the first
/// bytes instead, which is the part a format actually defines.
/// </para>
/// <para>
/// <b>SVG is absent on purpose.</b> It is XML, it may contain
/// <c>&lt;script&gt;</c>, and a browser executes it when the file is opened
/// directly. Cloudinary serves from its own hostname, so the damage is bounded
/// — but "bounded" is doing a lot of work in that sentence, and no product
/// photograph is an SVG.
/// </para>
/// <para>
/// The point of checking here rather than leaving it to Cloudinary is that this
/// is the last place we can refuse something without it existing on the
/// internet first.
/// </para>
/// </remarks>
public static class ImageFileInspector
{
    /// <summary>Longest signature below, so callers know how much to read.</summary>
    public const int HeaderBytes = 16;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Detected MIME type, or null when nothing here is recognised.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> header)
    {
        if (StartsWith(header, Jpeg))
        {
            return "image/jpeg";
        }

        if (StartsWith(header, Png))
        {
            return "image/png";
        }

        // RIFF....WEBP — the size field sits between the two markers, so both
        // ends have to be checked and the middle four bytes ignored.
        if (header.Length >= 12
            && header[..4].SequenceEqual("RIFF"u8)
            && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        // ISO base media: a length prefix, then "ftyp", then the brand. AVIF and
        // HEIC share the container, and HEIC matters here because it is what an
        // iPhone produces by default — refusing it would mean every photograph
        // taken on one has to be converted before it can be uploaded.
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = header[8..12];

            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
            {
                return "image/avif";
            }

            if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8)
                || brand.SequenceEqual("heim"u8) || brand.SequenceEqual("heis"u8)
                || brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8))
            {
                return "image/heic";
            }
        }

        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature) =>
        header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);
}
