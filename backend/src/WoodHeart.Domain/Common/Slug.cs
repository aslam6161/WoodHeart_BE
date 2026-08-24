using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WoodHeart.Domain.Common;

/// <summary>
/// A URL-safe identifier for a product, category or collection —
/// <c>segun-wood-king-size-bed</c>.
/// </summary>
/// <remarks>
/// Slugs are part of the public URL and therefore part of our SEO. Once a
/// product is published its slug is stable: changing it breaks every inbound
/// link and every Facebook share. Renaming a product must keep the original
/// slug (or add a redirect), which is why this is a value object with an
/// explicit creation step rather than a string recomputed from the name.
/// </remarks>
public sealed partial class Slug : ValueObject
{
    public const int MaxLength = 160;

    private Slug(string value) => Value = value;

    public string Value { get; }

    public static Slug From(string value)
    {
        var normalized = Normalize(value);

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Cannot build a slug from the supplied text.", nameof(value));
        }

        return new Slug(normalized);
    }

    /// <summary>Appends a numeric suffix to resolve a collision: <c>bed-2</c>.</summary>
    public Slug WithSuffix(int suffix)
    {
        var tail = $"-{suffix}";
        var head = Value.Length + tail.Length > MaxLength
            ? Value[..(MaxLength - tail.Length)].TrimEnd('-')
            : Value;

        return new Slug(head + tail);
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // Strip diacritics so "Décor" and "Decor" produce the same slug.
        var decomposed = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        var cleaned = NonSlugChars().Replace(builder.ToString().Normalize(NormalizationForm.FormC), "-");
        cleaned = MultipleDashes().Replace(cleaned, "-").Trim('-');

        return cleaned.Length > MaxLength ? cleaned[..MaxLength].TrimEnd('-') : cleaned;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(Slug slug) => slug.Value;

    // Bangla characters are kept: Bangla-language slugs are valid, indexable URLs.
    [GeneratedRegex(@"[^a-z0-9ঀ-৿]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultipleDashes();
}
