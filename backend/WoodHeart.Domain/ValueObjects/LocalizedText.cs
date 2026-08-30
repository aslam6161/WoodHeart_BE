namespace WoodHeart.Domain.ValueObjects;

/// <summary>
/// A piece of customer-facing text in English and Bangla.
/// </summary>
/// <remarks>
/// <para>
/// Persisted as a single JSONB column (<c>{"en": "...", "bn": "..."}</c>) rather
/// than a separate translations table. With only two languages that is one join
/// avoided on every product read, PostgreSQL indexes JSONB well, and adding a
/// third language later needs no schema change.
/// </para>
/// <para>
/// English is required; Bangla is optional and falls back to English, so the
/// team can launch in English and translate incrementally without ever
/// rendering an empty product name.
/// </para>
/// </remarks>
public sealed class LocalizedText : ValueObject
{
    public const string English = "en";
    public const string Bangla = "bn";

    private LocalizedText(string en, string? bn)
    {
        En = en;
        Bn = bn;
    }

    public string En { get; }

    public string? Bn { get; }

    public bool HasBangla => !string.IsNullOrWhiteSpace(Bn);

    public static LocalizedText Create(string en, string? bn = null)
    {
        if (string.IsNullOrWhiteSpace(en))
        {
            throw new ArgumentException("English text is required.", nameof(en));
        }

        return new LocalizedText(en.Trim(), string.IsNullOrWhiteSpace(bn) ? null : bn.Trim());
    }

    /// <summary>Resolves for a culture, falling back to English when Bangla is missing.</summary>
    public string For(string? languageCode) =>
        languageCode?.StartsWith(Bangla, StringComparison.OrdinalIgnoreCase) == true && HasBangla
            ? Bn!
            : En;

    public LocalizedText WithBangla(string? bn) => new(En, bn);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return En;
        yield return Bn;
    }

    public override string ToString() => En;

    public static implicit operator string(LocalizedText text) => text.En;
}
