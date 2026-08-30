using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Tests.Common;

public class SlugTests
{
    [Theory]
    [InlineData("Segun Wood King Size Bed", "segun-wood-king-size-bed")]
    [InlineData("  Dining  Table  (6 Seater)  ", "dining-table-6-seater")]
    [InlineData("Décor Mirror", "decor-mirror")]
    [InlineData("Basin Cabinet — Matte White", "basin-cabinet-matte-white")]
    [InlineData("100% Solid Wood!", "100-solid-wood")]
    public void Product_names_become_clean_urls(string name, string expected)
    {
        Slug.From(name).Value.ShouldBe(expected);
    }

    [Fact]
    public void Bangla_names_produce_usable_slugs()
    {
        // Bangla characters are valid, indexable URL content — stripping them
        // would collapse every Bangla product name to an empty slug.
        var slug = Slug.From("সেগুন কাঠের খাট");

        slug.Value.ShouldNotBeEmpty();
        slug.Value.ShouldNotContain(" ");
    }

    [Fact]
    public void WithSuffix_resolves_a_name_collision()
    {
        Slug.From("dining-table").WithSuffix(2).Value.ShouldBe("dining-table-2");
    }

    [Fact]
    public void Slugs_are_truncated_to_the_column_length()
    {
        var slug = Slug.From(new string('a', 300));

        slug.Value.Length.ShouldBeLessThanOrEqualTo(Slug.MaxLength);
    }

    [Fact]
    public void Text_with_nothing_sluggable_throws()
    {
        Should.Throw<ArgumentException>(() => Slug.From("!!!"));
        Should.Throw<ArgumentException>(() => Slug.From("   "));
    }
}
