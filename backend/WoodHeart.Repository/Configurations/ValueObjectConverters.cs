using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WoodHeart.Domain.ValueObjects;

// The static members below are deliberately named Slug/Money/LocalizedText to
// read well at the call site, which shadows the type names inside this class.
// The alias is how the converters still reach the real types.
using Vo = WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Repository.Configurations;

/// <summary>
/// Converters that let the domain's value objects be persisted as columns.
/// </summary>
/// <remarks>
/// <para>
/// The catalog is the first module to store <see cref="Money"/>,
/// <see cref="Slug"/> and <see cref="LocalizedText"/>, so these live here
/// rather than beside any one entity — orders, discounts and consultations will
/// all use them.
/// </para>
/// <para>
/// <b>Every converter is paired with a <see cref="ValueComparer{T}"/>, and that
/// is not optional.</b> EF Core tracks changes to a converted property by
/// comparing snapshots. For a reference type it defaults to reference equality,
/// so mutating or replacing one of these with an equal-but-distinct instance
/// would either be missed entirely or flagged as dirty on every save. The
/// comparers below make EF use the value semantics the types already implement.
/// </para>
/// </remarks>
public static class ValueObjectConverters
{
    /// <summary>
    /// JSON options for the localized-text column. Bangla must not be escaped
    /// into <c>\uXXXX</c> sequences: it triples the stored size and makes the
    /// column unreadable in psql or pgAdmin, which is where you look when
    /// something is wrong.
    /// </summary>
    private static readonly JsonSerializerOptions LocalizedJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    // --- Slug ---------------------------------------------------------------

    public static readonly ValueConverter<Slug, string> Slug =
        new(slug => slug.Value, value => Vo.Slug.From(value));

    public static readonly ValueComparer<Slug> SlugComparer =
        new(
            (left, right) => left!.Value == right!.Value,
            slug => slug.Value.GetHashCode(StringComparison.Ordinal),
            slug => Vo.Slug.From(slug.Value));

    // --- Money --------------------------------------------------------------

    /// <summary>
    /// Stores the amount only, and reconstructs it as BDT.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This deliberately drops the currency.</b> v1 is BDT-only — PLAN.md §3
    /// commits to that — and a <c>currency</c> column repeating 'BDT' beside
    /// every price, surcharge and total would cost a column per money field
    /// across the whole schema to encode a constant.
    /// </para>
    /// <para>
    /// The guard that matters is still in place: <see cref="Vo.Money"/>
    /// itself refuses to add BDT to USD, so nothing in the domain can produce a
    /// mixed-currency total. What is lost is only the ability to *store* a
    /// second currency, and the day that is needed this becomes an owned type
    /// with an added <c>currency</c> column — an additive migration with a
    /// constant backfill, not a redesign.
    /// </para>
    /// </remarks>
    public static readonly ValueConverter<Money, decimal> Money =
        new(money => money.Amount, amount => Vo.Money.Taka(amount));

    public static readonly ValueComparer<Money> MoneyComparer =
        new(
            (left, right) => left!.Amount == right!.Amount && left.Currency == right.Currency,
            money => HashCode.Combine(money.Amount, money.Currency),
            money => Vo.Money.From(money.Amount, money.Currency));

    // --- LocalizedText ------------------------------------------------------

    /// <summary>
    /// Stores <c>{"en":"...","bn":"..."}</c> in a single jsonb column.
    /// </summary>
    /// <remarks>
    /// jsonb rather than a translations table: two languages, and a join per
    /// translated field on every product read is a poor trade. PLAN.md §6 and
    /// the <see cref="LocalizedText"/> docs cover the reasoning.
    /// </remarks>
    public static readonly ValueConverter<LocalizedText, string> LocalizedText =
        new(
            text => JsonSerializer.Serialize(new LocalizedTextJson(text.En, text.Bn), LocalizedJson),
            json => FromJson(json));

    public static readonly ValueComparer<LocalizedText> LocalizedTextComparer =
        new(
            (left, right) => left!.En == right!.En && left.Bn == right.Bn,
            text => HashCode.Combine(text.En, text.Bn),
            text => Vo.LocalizedText.Create(text.En, text.Bn));

    // --- Option values ------------------------------------------------------

    /// <summary>
    /// Stores a variant's option map — <c>{"Wood":"Segun","Size":"6ft"}</c> —
    /// as jsonb so it can be GIN indexed and queried with the containment
    /// operator.
    /// </summary>
    public static readonly ValueConverter<Dictionary<string, string>, string> OptionValues =
        new(
            options => JsonSerializer.Serialize(options, LocalizedJson),
            json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, LocalizedJson)
                    ?? new Dictionary<string, string>());

    /// <summary>
    /// Compares by content and snapshots by copying.
    /// </summary>
    /// <remarks>
    /// The snapshot must be a genuine copy. Without it EF holds a reference to
    /// the live dictionary, so the "original" and "current" values are the same
    /// object, every comparison says nothing changed, and edits to a variant's
    /// options are silently never saved.
    /// </remarks>
    public static readonly ValueComparer<Dictionary<string, string>> OptionValuesComparer =
        new(
            (left, right) => left!.Count == right!.Count && !left.Except(right).Any(),
            options => options.Aggregate(
                0,
                (hash, pair) => HashCode.Combine(hash, pair.Key.GetHashCode(StringComparison.Ordinal), pair.Value.GetHashCode(StringComparison.Ordinal))),
            options => new Dictionary<string, string>(options, StringComparer.Ordinal));

    private static LocalizedText FromJson(string json)
    {
        var parsed = JsonSerializer.Deserialize<LocalizedTextJson>(json, LocalizedJson);

        return parsed is null || string.IsNullOrWhiteSpace(parsed.En)
            ? Vo.LocalizedText.Create("(untitled)")
            : Vo.LocalizedText.Create(parsed.En, parsed.Bn);
    }

    /// <summary>
    /// The stored shape: <c>{"en":"...","bn":"..."}</c>.
    /// </summary>
    /// <remarks>
    /// The JSON names are pinned with attributes rather than by naming the
    /// members in lower case. A record that declared both a positional <c>en</c>
    /// and a property <c>En</c> cannot be deserialized at all — System.Text.Json
    /// refuses to bind two members to one constructor parameter — and the
    /// failure surfaces on the first write, not at compile time.
    /// </remarks>
    private sealed record LocalizedTextJson(
        [property: JsonPropertyName("en")] string En,
        [property: JsonPropertyName("bn")] string? Bn);
}
