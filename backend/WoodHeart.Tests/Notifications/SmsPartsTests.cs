using WoodHeart.Service.Infrastructure.Notifications;

namespace WoodHeart.Tests.Notifications;

/// <summary>
/// How many parts a message is billed as.
/// </summary>
/// <remarks>
/// <b>This is arithmetic about money.</b> A GSM-7 message is 160 characters,
/// or 153 each once it splits. One Bangla character switches the whole message
/// to UCS-2, where the limits are 70 and 67 — so a single "৳" appended to an
/// English confirmation can more than double what every order costs to
/// acknowledge.
/// </remarks>
public class SmsPartsTests
{
    [Fact]
    public void Nothing_costs_nothing() =>
        SmsParts.Count("").ShouldBe(0);

    [Fact]
    public void A_short_English_message_is_one_part() =>
        SmsParts.Count("WoodHeart: Order WH-2609-00042 confirmed.").ShouldBe(1);

    [Fact]
    public void English_is_one_part_up_to_160_characters()
    {
        SmsParts.Count(new string('a', 160)).ShouldBe(1);
        SmsParts.Count(new string('a', 161)).ShouldBe(2);
    }

    [Fact]
    public void Once_split_each_part_holds_153_not_160()
    {
        // The concatenation header eats seven characters of every part. A
        // message of 306 is two parts; 307 is three.
        SmsParts.Count(new string('a', 306)).ShouldBe(2);
        SmsParts.Count(new string('a', 307)).ShouldBe(3);
    }

    [Fact]
    public void One_Bangla_character_re_prices_the_whole_message()
    {
        // The rule worth knowing before writing a template. 100 plain
        // characters is one part; the same message with a single "৳" in it is
        // two, because the encoding changes for the entire message rather than
        // for that character.
        var english = new string('a', 100);

        SmsParts.Count(english).ShouldBe(1);
        SmsParts.Count(english + "৳").ShouldBe(2);
    }

    [Fact]
    public void Bangla_is_one_part_up_to_70_characters()
    {
        SmsParts.Count(new string('অ', 70)).ShouldBe(1);
        SmsParts.Count(new string('অ', 71)).ShouldBe(2);
    }

    [Fact]
    public void An_emoji_counts_as_the_two_units_the_gateway_bills_for()
    {
        // Outside the basic plane, so it is a surrogate pair — two UCS-2 units,
        // which is what the gateway charges on. 35 of them is 70 units, one
        // part; 36 is 72, two.
        SmsParts.Count(string.Concat(Enumerable.Repeat("🪑", 35))).ShouldBe(1);
        SmsParts.Count(string.Concat(Enumerable.Repeat("🪑", 36))).ShouldBe(2);
    }

    [Fact]
    public void The_description_says_which_encoding_was_assumed()
    {
        SmsParts.Describe("Order confirmed").ShouldBe("1 part(s) (GSM-7)");
        SmsParts.Describe("অর্ডার").ShouldBe("1 part(s) (Unicode)");
    }
}
