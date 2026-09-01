using WoodHeart.Service.Services.Media;

namespace WoodHeart.Tests.Catalog;

/// <summary>
/// The check that decides whether bytes reach Cloudinary at all.
/// </summary>
public class ImageFileInspectorTests
{
    private static byte[] Header(params byte[] bytes)
    {
        var header = new byte[ImageFileInspector.HeaderBytes];
        bytes.CopyTo(header, 0);
        return header;
    }

    [Fact]
    public void Recognises_a_jpeg()
    {
        ImageFileInspector.DetectContentType(Header(0xFF, 0xD8, 0xFF, 0xE0))
            .ShouldBe("image/jpeg");
    }

    [Fact]
    public void Recognises_a_png()
    {
        ImageFileInspector.DetectContentType(
                Header(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
            .ShouldBe("image/png");
    }

    [Fact]
    public void Recognises_a_webp_despite_the_size_field_in_the_middle()
    {
        // RIFF, then four bytes of length that are different for every file,
        // then WEBP. A naive prefix check on "RIFF" alone also matches a WAV.
        var header = Header();
        "RIFF"u8.CopyTo(header);
        header[4] = 0x2A;
        header[5] = 0x13;
        header[6] = 0x00;
        header[7] = 0x00;
        "WEBP"u8.CopyTo(header.AsSpan(8));

        ImageFileInspector.DetectContentType(header).ShouldBe("image/webp");
    }

    [Fact]
    public void Recognises_a_heic_because_that_is_what_an_iPhone_produces()
    {
        // Refusing HEIC would mean every photograph taken on an iPhone has to
        // be converted before anyone can upload it.
        var header = Header();
        "ftyp"u8.CopyTo(header.AsSpan(4));
        "heic"u8.CopyTo(header.AsSpan(8));

        ImageFileInspector.DetectContentType(header).ShouldBe("image/heic");
    }

    [Fact]
    public void Recognises_an_avif()
    {
        var header = Header();
        "ftyp"u8.CopyTo(header.AsSpan(4));
        "avif"u8.CopyTo(header.AsSpan(8));

        ImageFileInspector.DetectContentType(header).ShouldBe("image/avif");
    }

    [Fact]
    public void Refuses_an_svg_however_it_is_named()
    {
        // SVG is XML, it can carry <script>, and a browser runs it when the
        // file is opened directly. No product photograph is an SVG, so the
        // format is simply absent from the allow list rather than sanitised.
        var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\">");

        ImageFileInspector.DetectContentType(svg.AsSpan(0, ImageFileInspector.HeaderBytes))
            .ShouldBeNull();
    }

    [Fact]
    public void Refuses_a_php_script_named_jpg()
    {
        // The whole reason this class exists: the file name is caller input and
        // proves nothing. These bytes would be uploaded happily by any check
        // that trusted the extension.
        var script = System.Text.Encoding.UTF8.GetBytes("<?php system($_GET['c']); ?>");

        ImageFileInspector.DetectContentType(script.AsSpan(0, ImageFileInspector.HeaderBytes))
            .ShouldBeNull();
    }

    [Fact]
    public void Refuses_an_empty_or_truncated_header()
    {
        ImageFileInspector.DetectContentType([]).ShouldBeNull();
        ImageFileInspector.DetectContentType([0xFF, 0xD8]).ShouldBeNull();
    }
}
