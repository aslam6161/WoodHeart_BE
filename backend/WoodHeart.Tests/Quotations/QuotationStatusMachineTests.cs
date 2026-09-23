using WoodHeart.Domain.Enums.Quotations;
using WoodHeart.Domain.Quotations;

namespace WoodHeart.Tests.Quotations;

/// <summary>
/// Which way a quotation may move, and the two questions the screens ask of
/// it: may this be edited, and may the customer still answer it.
/// </summary>
public class QuotationStatusMachineTests
{
    [Fact]
    public void A_draft_can_only_be_sent()
    {
        QuotationStatusMachine.NextFrom(QuotationStatus.Draft)
            .ShouldBe([QuotationStatus.Sent]);
    }

    [Fact]
    public void A_sent_quotation_can_be_pulled_back_and_changed()
    {
        // "Can you add the two bedside tables" is the commonest thing that
        // happens to a quotation, and the shop has to be able to answer it.
        QuotationStatusMachine.CanTransition(QuotationStatus.Sent, QuotationStatus.Draft)
            .ShouldBe(true);
    }

    [Fact]
    public void An_accepted_one_cannot_be_edited_back_into_a_draft()
    {
        // At that point the figures are an agreement. A new agreement is a new
        // quotation, not a quiet edit of the one somebody said yes to.
        QuotationStatusMachine.CanTransition(QuotationStatus.Accepted, QuotationStatus.Draft)
            .ShouldBe(false);

        QuotationStatusMachine.IsEditable(QuotationStatus.Accepted).ShouldBe(false);
    }

    [Fact]
    public void Only_a_draft_is_editable()
    {
        foreach (var status in Enum.GetValues<QuotationStatus>())
        {
            QuotationStatusMachine.IsEditable(status)
                .ShouldBe(status == QuotationStatus.Draft, status.ToString());
        }
    }

    [Fact]
    public void Only_a_sent_one_is_the_customers_to_answer()
    {
        foreach (var status in Enum.GetValues<QuotationStatus>())
        {
            QuotationStatusMachine.IsAnswerable(status)
                .ShouldBe(status == QuotationStatus.Sent, status.ToString());
        }
    }

    [Fact]
    public void Somebody_who_said_no_last_week_can_be_quoted_again()
    {
        // Kinder than making them ask twice, and it keeps the history on one
        // record rather than scattering it over two.
        QuotationStatusMachine.CanTransition(QuotationStatus.Declined, QuotationStatus.Draft)
            .ShouldBe(true);
    }

    [Fact]
    public void An_expired_one_is_re_quoted_rather_than_honoured()
    {
        QuotationStatusMachine.CanTransition(QuotationStatus.Expired, QuotationStatus.Draft)
            .ShouldBe(true);

        QuotationStatusMachine.CanTransition(QuotationStatus.Expired, QuotationStatus.Accepted)
            .ShouldBe(false);
    }

    [Fact]
    public void Converted_is_the_end_of_the_line()
    {
        QuotationStatusMachine.IsTerminal(QuotationStatus.Converted).ShouldBe(true);
        QuotationStatusMachine.NextFrom(QuotationStatus.Converted).ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Running out of time
    // -------------------------------------------------------------------------

    [Fact]
    public void A_quotation_lapses_the_day_after_it_is_good_until()
    {
        var until = new DateOnly(2026, 10, 15);

        QuotationStatusMachine.HasLapsed(QuotationStatus.Sent, until, until).ShouldBe(false);

        // The whole of the 15th, as the shop lives it — not until six in the
        // evening, which is what an instant would have meant.
        QuotationStatusMachine.HasLapsed(QuotationStatus.Sent, until, until.AddDays(1))
            .ShouldBe(true);
    }

    [Fact]
    public void Only_something_still_out_with_the_customer_can_lapse()
    {
        var until = new DateOnly(2026, 10, 15);
        var later = until.AddDays(30);

        // A draft nobody sent has not run out of anything, and one already
        // accepted is an agreement rather than an offer.
        QuotationStatusMachine.HasLapsed(QuotationStatus.Draft, until, later).ShouldBe(false);
        QuotationStatusMachine.HasLapsed(QuotationStatus.Accepted, until, later).ShouldBe(false);
        QuotationStatusMachine.HasLapsed(QuotationStatus.Converted, until, later).ShouldBe(false);
    }
}
