using Shouldly;
using WoodHeart.Domain.Helpers;

namespace WoodHeart.Tests.Catalog;

/// <summary>
/// The shop's search vocabulary.
/// </summary>
/// <remarks>
/// This is the part of search that is a judgement call rather than a mechanism,
/// and the part most likely to be edited by whoever notices a customer typing
/// something nobody thought of. These tests are the record of what it is
/// supposed to do, so a word added to one group cannot quietly widen another.
/// </remarks>
public class SearchTermsTests
{
    private static IReadOnlyList<string> Flatten(string search) =>
        SearchTerms.Expand(search).SelectMany(group => group).ToList();

    // -------------------------------------------------------------------------
    // Romanised Bangla, which is how most of this shop's customers type
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("almari", "wardrobe")]
    [InlineData("almirah", "wardrobe")]
    [InlineData("cupboard", "wardrobe")]
    [InlineData("khat", "bed")]
    [InlineData("ayna", "mirror")]
    [InlineData("bati", "light")]
    [InlineData("nakshi", "art")]
    [InlineData("segun", "teak")]
    [InlineData("bet", "rattan")]
    public void A_word_a_customer_types_finds_the_word_the_catalogue_uses(
        string typed, string catalogued)
    {
        // The whole point. "almari" and "wardrobe" share no letters, so no
        // amount of fuzzy matching gets from one to the other — it takes a
        // vocabulary.
        Flatten(typed).ShouldContain(catalogued);
    }

    [Fact]
    public void Bangla_script_is_in_the_vocabulary_too()
    {
        Flatten("আলমারি").ShouldContain("wardrobe");
        Flatten("খাট").ShouldContain("bed");
    }

    [Fact]
    public void The_relationship_runs_both_ways()
    {
        // A customer searching in English must still find a product whose text
        // is Bangla, which is the same lookup in the other direction.
        Flatten("wardrobe").ShouldContain("আলমারি");
    }

    // -------------------------------------------------------------------------
    // Words, not a phrase
    // -------------------------------------------------------------------------

    [Fact]
    public void Every_word_becomes_its_own_requirement()
    {
        var groups = SearchTerms.Expand("dining chair");

        // Two groups means two ANDed conditions, which is what makes word order
        // irrelevant. One group would be a single substring match, and
        // "chair, dining" would find nothing.
        groups.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("sofa set")]
    [InlineData("sofa for my room")]
    [InlineData("i want a wardrobe")]
    [InlineData("wooden bed price in bd")]
    public void Filler_words_are_dropped_rather_than_demanded(string search)
    {
        // Every word has to match, so one word no product is described by sinks
        // the whole query. "sofa set" returning nothing is the bug this list
        // exists to prevent.
        var groups = SearchTerms.Expand(search);

        groups.ShouldAllBe(group => !group.Contains("set") && !group.Contains("room"));
        groups.Count.ShouldBeLessThan(search.Split(' ').Length);
    }

    [Fact]
    public void Punctuation_and_case_are_not_search_terms()
    {
        SearchTerms.Tokenize("Dining-Table, \"six seat\"")
            .ShouldBe(["dining", "table", "six", "seat"]);
    }

    [Fact]
    public void A_single_letter_is_not_a_search_term()
    {
        // It matches nearly every product, so it filters nothing and costs a
        // scan to discover that.
        SearchTerms.Tokenize("a b wardrobe").ShouldBe(["wardrobe"]);
    }

    [Fact]
    public void An_empty_search_asks_for_nothing()
    {
        SearchTerms.Expand(null).ShouldBeEmpty();
        SearchTerms.Expand("   ").ShouldBeEmpty();
        SearchTerms.Expand("the and for").ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Correction, which only suggestions use
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("wardorbe", "wardrobe")]
    [InlineData("almri", "wardrobe")]
    [InlineData("cupbord", "wardrobe")]
    [InlineData("khaat", "bed")]
    [InlineData("dinning", "dining")]
    public void A_near_miss_is_pulled_to_the_word_the_catalogue_uses(
        string typed, string expected)
    {
        // Suggestions are scored against the text in the database, which is
        // written in the catalogue's own words — so a typo has to be corrected
        // into those words before similarity has anything to score.
        SearchTerms.Correct(typed).ShouldBe(expected);
    }

    [Theory]
    [InlineData("tub", "planter")]
    [InlineData("bed", "bed")]
    [InlineData("pot", "planter")]
    [InlineData("cot", "cot")]
    public void A_short_word_is_never_corrected_into_a_different_one(
        string typed, string expected)
    {
        // One edit is the entire distance between "set" and "bet", and "bet" is
        // cane furniture. Correcting three-letter words is what made "sofa set"
        // suggest a rattan pendant light. A short word is either in the
        // vocabulary — "tub" is a planter — or left exactly as typed.
        SearchTerms.Correct(typed).ShouldBe(expected);
    }

    [Fact]
    public void A_word_nothing_resembles_is_left_alone()
    {
        // Correcting "helicopter" to the nearest piece of furniture would be
        // worse than admitting the shop does not sell one.
        SearchTerms.Correct("helicopter").ShouldBe("helicopter");
    }
}
