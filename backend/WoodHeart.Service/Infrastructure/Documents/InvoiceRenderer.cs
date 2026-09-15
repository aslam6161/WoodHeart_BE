using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Infrastructure.Documents;

/// <summary>
/// Turns an <see cref="InvoiceModel"/> into PDF bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one-time setup is here rather than in <c>Program</c>.</b> QuestPDF
/// refuses to draw anything until a licence tier is declared and, with
/// environment fonts switched off, until the fonts are registered — so the two
/// belong beside the only code that draws, where they cannot be lost in a
/// refactor of the startup file.
/// </para>
/// <para>
/// <b>Environment fonts are switched off deliberately.</b> Left on, a developer
/// on Windows renders through Segoe UI and a container renders through nothing
/// at all, so the first time anyone sees the real output is when a customer
/// opens the box. Off, the only fonts in play are the four embedded ones and
/// every machine produces the same page.
/// </para>
/// </remarks>
public static class InvoiceRenderer
{
    /// <summary>Latin. Everything the shop writes itself.</summary>
    public const string LatinFont = "Noto Sans";

    /// <summary>
    /// Bangla. Reached as a fallback, not chosen.
    /// </summary>
    /// <remarks>
    /// The Latin family has no Bengali glyphs and the Bengali family has almost
    /// no Latin ones, so the pair is a chain rather than a choice: names and
    /// addresses the customer typed resolve through the second, and everything
    /// else through the first. This is also why the library draws through
    /// HarfBuzz — Bangla needs conjuncts formed and vowel signs reordered, and a
    /// renderer that merely places glyphs left to right produces the right
    /// letters in an order nobody can read.
    /// </remarks>
    public const string BanglaFont = "Noto Sans Bengali";

    private static readonly Lock Gate = new();

    private static bool _ready;

    /// <summary>Draws the document. Thread-safe; sets the library up on first use.</summary>
    public static byte[] Render(InvoiceModel model)
    {
        Prepare();

        return new InvoiceDocument(model).GeneratePdf();
    }

    /// <summary>
    /// Declares the licence and registers the fonts, once per process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Community licence.</b> QuestPDF is free for organisations under $1M
    /// USD annual revenue, which WoodHeart is by a wide margin. If the shop ever
    /// passes that line this line is what has to change, which is why it is a
    /// statement in code rather than a setting nobody would find.
    /// </para>
    /// <para>
    /// Glyph checking is left at its default — on only when a debugger is
    /// attached. A developer then hears about a character the fonts cannot draw,
    /// while a customer's emoji in an address line renders as a placeholder box
    /// instead of failing the request that was going to give them their invoice.
    /// </para>
    /// </remarks>
    public static void Prepare()
    {
        if (_ready)
        {
            return;
        }

        lock (Gate)
        {
            if (_ready)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseEnvironmentFonts = false;

            var assembly = typeof(InvoiceRenderer).GetTypeInfo().Assembly;

            foreach (var resource in assembly.GetManifestResourceNames()
                         .Where(name => name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                FontManager.RegisterFontFromEmbeddedResource(resource);
            }

            _ready = true;
        }
    }
}
