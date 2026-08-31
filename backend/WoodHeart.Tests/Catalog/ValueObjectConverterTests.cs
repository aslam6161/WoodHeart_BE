using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository.Configurations;

namespace WoodHeart.Tests.Catalog;

/// <summary>
/// The converters that put value objects into columns.
/// </summary>
/// <remarks>
/// These get their own tests because every failure mode here is silent. A
/// broken comparer does not throw — it just stops saving edits, or marks every
/// row dirty on every request. A broken round-trip does not throw either; it
/// writes a product name that reads back as mojibake.
/// </remarks>
public class ValueObjectConverterTests
{
    [Fact]
    public void Slug_round_trips()
    {
        var slug = Slug.From("Segun Wood King Size Bed");

        var stored = ValueObjectConverters.Slug.ConvertToProvider(slug);
        stored.ShouldBe("segun-wood-king-size-bed");

        var restored = (Slug)ValueObjectConverters.Slug.ConvertFromProvider(stored)!;
        restored.Value.ShouldBe(slug.Value);
    }

    [Fact]
    public void Money_round_trips_as_a_decimal_amount()
    {
        var price = Money.Taka(45_000.50m);

        var stored = ValueObjectConverters.Money.ConvertToProvider(price);
        stored.ShouldBe(45_000.50m);

        var restored = (Money)ValueObjectConverters.Money.ConvertFromProvider(stored)!;
        restored.Amount.ShouldBe(45_000.50m);
        restored.Currency.ShouldBe(Money.Bdt);
    }

    [Fact]
    public void Localized_text_round_trips_both_languages()
    {
        var name = LocalizedText.Create("Segun King Bed", "সেগুন কিং বেড");

        var json = (string)ValueObjectConverters.LocalizedText.ConvertToProvider(name)!;
        var restored = (LocalizedText)ValueObjectConverters.LocalizedText.ConvertFromProvider(json)!;

        restored.En.ShouldBe("Segun King Bed");
        restored.Bn.ShouldBe("সেগুন কিং বেড");
    }

    [Fact]
    public void Bangla_is_stored_unescaped()
    {
        // Not cosmetic. Escaped to \uXXXX this roughly triples the stored bytes
        // on every translated column, and makes the value unreadable in psql or
        // pgAdmin — which is exactly where you look when something is wrong.
        var name = LocalizedText.Create("Bed", "বেড");

        var json = (string)ValueObjectConverters.LocalizedText.ConvertToProvider(name)!;

        json.ShouldContain("বেড");
        json.ShouldNotContain("\\u");
    }

    [Fact]
    public void Localized_text_without_bangla_round_trips_as_null()
    {
        var name = LocalizedText.Create("Side Table");

        var json = (string)ValueObjectConverters.LocalizedText.ConvertToProvider(name)!;
        var restored = (LocalizedText)ValueObjectConverters.LocalizedText.ConvertFromProvider(json)!;

        restored.Bn.ShouldBeNull();
        restored.HasBangla.ShouldBeFalse();
        restored.For("bn").ShouldBe("Side Table");
    }

    [Fact]
    public void Option_values_round_trip()
    {
        var options = new Dictionary<string, string> { ["Wood"] = "Segun", ["Size"] = "6ft" };

        var json = (string)ValueObjectConverters.OptionValues.ConvertToProvider(options)!;
        var restored = (Dictionary<string, string>)ValueObjectConverters.OptionValues.ConvertFromProvider(json)!;

        restored.Count.ShouldBe(2);
        restored["Wood"].ShouldBe("Segun");
        restored["Size"].ShouldBe("6ft");
    }

    [Fact]
    public void Money_comparer_sees_equal_amounts_as_unchanged()
    {
        // Without this, EF compares by reference and marks every loaded product
        // as modified on every request, issuing an UPDATE per row per page view.
        var comparer = ValueObjectConverters.MoneyComparer;

        comparer.Equals(Money.Taka(100m), Money.Taka(100m)).ShouldBeTrue();
        comparer.Equals(Money.Taka(100m), Money.Taka(100.01m)).ShouldBeFalse();
    }

    [Fact]
    public void Localized_text_comparer_distinguishes_a_bangla_edit()
    {
        // The failure this prevents: an admin adds the Bangla name, the English
        // is unchanged, and a comparer that only looked at English would decide
        // nothing happened and silently discard the translation.
        var comparer = ValueObjectConverters.LocalizedTextComparer;

        var english = LocalizedText.Create("Bed");
        var translated = LocalizedText.Create("Bed", "বেড");

        comparer.Equals(english, LocalizedText.Create("Bed")).ShouldBeTrue();
        comparer.Equals(english, translated).ShouldBeFalse();
    }

    [Fact]
    public void Option_values_snapshot_is_a_copy_not_a_reference()
    {
        // The bug this exists to prevent: if the snapshot aliases the live
        // dictionary, EF's "original" and "current" values are the same object,
        // every comparison says nothing changed, and edits to a variant's
        // options are never written.
        var comparer = ValueObjectConverters.OptionValuesComparer;
        var live = new Dictionary<string, string> { ["Wood"] = "Segun" };

        var snapshot = comparer.Snapshot(live);
        live["Wood"] = "Mehogoni";

        snapshot["Wood"].ShouldBe("Segun");
        comparer.Equals(snapshot, live).ShouldBeFalse();
    }

    [Fact]
    public void Option_values_comparer_ignores_key_order()
    {
        var comparer = ValueObjectConverters.OptionValuesComparer;

        var a = new Dictionary<string, string> { ["Wood"] = "Segun", ["Size"] = "6ft" };
        var b = new Dictionary<string, string> { ["Size"] = "6ft", ["Wood"] = "Segun" };

        comparer.Equals(a, b).ShouldBeTrue();
    }
}
