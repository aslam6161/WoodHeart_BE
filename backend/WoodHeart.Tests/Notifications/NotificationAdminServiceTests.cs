using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Entity.Notifications;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Domain.Enums.Notifications;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Repository.Interfaces.Notifications;
using WoodHeart.Service.DTOs.Notifications;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Services.Notifications;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Notifications;

/// <summary>
/// The screen that decides what the shop says, and says what happened when it
/// said it.
/// </summary>
/// <remarks>
/// <para>
/// The behaviours worth protecting are the ones a list of switches would get
/// wrong. <b>The preview is the real message</b> — rendered by the same code
/// the delivery worker uses, so somebody deciding whether an SMS is worth
/// paying for is reading what would be sent rather than a description of it.
/// <b>The part count is the price</b>, and the English and Bangla forms of the
/// same message are frequently one part and three.
/// </para>
/// <para>
/// And on the messages side: <b>a message that is already coming cannot be
/// resent</b>, because a button that does nothing but look as though it did is
/// worse than no button, and one a worker is holding would be sent twice.
/// </para>
/// </remarks>
public class NotificationAdminServiceTests
{
    private readonly INotificationTemplateRepository _templates =
        Substitute.For<INotificationTemplateRepository>();

    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();

    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly FakeClock _clock = new();

    private readonly SmsSettings _sms = new() { ApiKey = "a-key" };

    private readonly EmailSettings _email = new()
    {
        Host = "smtp.example.com",
        FromAddress = "shop@example.com"
    };

    public NotificationAdminServiceTests()
    {
        _settings.GetStringAsync(SettingKeys.StorePhone, Arg.Any<CancellationToken>())
            .Returns("01712345678");

        _templates.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<NotificationTemplate>());

        Run<NotificationTemplateDetailDto>();
        Run<NotificationMessageDto>();
    }

    /// <summary>
    /// The substitute unit of work runs the delegate rather than opening a
    /// transaction, so these tests exercise the body.
    /// </summary>
    private void Run<T>() =>
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<T>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse<T>>>>()(CancellationToken.None));

    private NotificationAdminService Create() =>
        new(_templates,
            _outbox,
            _settings,
            _clock,
            _unitOfWork,
            Options.Create(_sms),
            Options.Create(_email),
            NullLogger<NotificationAdminService>.Instance);

    // -------------------------------------------------------------------------
    // What the shop sends
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Every_message_the_worker_can_render_is_on_the_screen()
    {
        // The list is the catalogue, not the table. A kind of message the code
        // can send and the screen does not show is one nobody can turn off.
        var response = await Create().GetTemplatesAsync();

        response.IsSuccess.ShouldBeTrue();

        response.Data!.Templates
            .Select(t => t.Code)
            .OrderBy(c => c)
            .ShouldBe(NotificationTemplates.KnownTypes.OrderBy(c => c));
    }

    [Fact]
    public async Task A_template_with_no_row_reads_as_on()
    {
        var response = await Create().GetTemplatesAsync();

        response.Data!.Templates.ShouldAllBe(t => t.SmsEnabled && t.EmailEnabled);
    }

    [Fact]
    public async Task The_switches_come_from_the_row_where_there_is_one()
    {
        _templates.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<NotificationTemplate>>(
            [
                new NotificationTemplate
                {
                    Code = NotificationTemplates.QuotationSent,
                    SmsEnabled = false,
                    EmailEnabled = true
                }
            ]);

        var response = await Create().GetTemplatesAsync();

        var quotation = response.Data!.Templates
            .Single(t => t.Code == NotificationTemplates.QuotationSent);

        quotation.SmsEnabled.ShouldBeFalse();
        quotation.EmailEnabled.ShouldBeTrue();

        // And nothing else was turned off by association.
        response.Data.Templates
            .Where(t => t.Code != NotificationTemplates.QuotationSent)
            .ShouldAllBe(t => t.SmsEnabled);
    }

    [Fact]
    public async Task A_gateway_with_no_key_is_reported_rather_than_assumed_working()
    {
        // The logging sender reports success, which is right on a developer's
        // machine and disastrous to discover in production from a customer.
        _sms.ApiKey = string.Empty;

        var response = await Create().GetTemplatesAsync();

        response.Data!.SmsGatewayConfigured.ShouldBeFalse();
        response.Data.EmailConfigured.ShouldBeTrue();
    }

    [Fact]
    public async Task A_shop_with_no_telephone_number_set_is_told_here()
    {
        // Every message ends with it. Unset, the confirmation stops
        // mid-sentence and the email says "Any questions, please call ." — to
        // every customer, on every order, and nothing anywhere complains. This
        // is the screen where that is visible, because the preview shows the
        // gap.
        _settings.GetStringAsync(SettingKeys.StorePhone, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var response = await Create().GetTemplatesAsync();

        response.Data!.ShopPhoneConfigured.ShouldBeFalse();

        var detail = await Create().GetTemplateAsync(NotificationTemplates.OrderPlaced);

        detail.Data!.ShopPhoneConfigured.ShouldBeFalse();
    }

    [Fact]
    public async Task A_number_of_nothing_but_spaces_is_not_a_number()
    {
        // The renderer trims it, so a setting of "   " produces exactly the
        // same dangling sentence as an empty one.
        _settings.GetStringAsync(SettingKeys.StorePhone, Arg.Any<CancellationToken>())
            .Returns("   ");

        var response = await Create().GetTemplatesAsync();

        response.Data!.ShopPhoneConfigured.ShouldBeFalse();
    }

    [Fact]
    public async Task And_one_that_is_set_is_reported_as_set()
    {
        var response = await Create().GetTemplatesAsync();

        response.Data!.ShopPhoneConfigured.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // The preview, which is the point of the screen
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_preview_is_the_message_itself()
    {
        var response = await Create().GetTemplateAsync(NotificationTemplates.OrderPlaced);

        var english = response.Data!.Previews.Single(p => p.Language == "en");

        english.SmsText!.ShouldContain("WH-2609-00042");
        english.SmsText!.ShouldContain("01712345678");
        english.EmailSubject.ShouldNotBeNullOrWhiteSpace();
        english.IsUnicode.ShouldBeFalse();
    }

    [Fact]
    public async Task A_Bangla_message_costs_more_parts_than_the_same_one_in_English()
    {
        // This is the decision the screen exists to support. One non-ASCII
        // character drops the billed part from 160 characters to 70, so the
        // Bangla form of a one-part confirmation is usually three — and no
        // template name anywhere carries that fact.
        var response = await Create().GetTemplateAsync(NotificationTemplates.OrderPlaced);

        var english = response.Data!.Previews.Single(p => p.Language == "en");
        var bangla = response.Data.Previews.Single(p => p.Language == "bn");

        bangla.IsUnicode.ShouldBeTrue();
        bangla.SmsParts.ShouldBeGreaterThan(english.SmsParts);

        response.Data.SmsParts.ShouldBe(english.SmsParts);
        response.Data.BanglaSmsParts.ShouldBe(bangla.SmsParts);
    }

    [Fact]
    public async Task The_message_written_for_the_shop_has_no_Bangla_form_and_says_so()
    {
        // It goes to whoever runs the shop, and an English one fits in a part
        // where a Bangla one would take three.
        var response = await Create().GetTemplateAsync(NotificationTemplates.StockLow);

        response.Data!.Audience.ShouldBe(NotificationAudience.Shop);
        response.Data.BanglaSmsParts.ShouldBeNull();
        response.Data.Previews.Select(p => p.Language).ShouldBe(["en"]);
    }

    [Fact]
    public async Task A_kind_of_message_nothing_sends_is_a_not_found()
    {
        var response = await Create().GetTemplateAsync("consultation.reminder");

        response.IsSuccess.ShouldBeFalse();
        response.ErrorCode.ShouldBe(NotificationErrors.TemplateNotFound);
    }

    // -------------------------------------------------------------------------
    // Turning one off
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Turning_a_channel_off_is_written_down()
    {
        var row = new NotificationTemplate { Code = NotificationTemplates.QuotationSent };

        _templates.GetByCodeAsync(NotificationTemplates.QuotationSent, Arg.Any<CancellationToken>())
            .Returns(row);

        var response = await Create().UpdateTemplateAsync(
            NotificationTemplates.QuotationSent,
            new UpdateNotificationTemplateDto { SmsEnabled = false, EmailEnabled = true });

        response.IsSuccess.ShouldBeTrue();
        row.SmsEnabled.ShouldBeFalse();
        row.EmailEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_template_the_seed_has_not_caught_up_with_is_created_on_first_edit()
    {
        _templates.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((NotificationTemplate?)null);

        var response = await Create().UpdateTemplateAsync(
            NotificationTemplates.BookingReminder,
            new UpdateNotificationTemplateDto { SmsEnabled = true, EmailEnabled = false });

        response.IsSuccess.ShouldBeTrue();

        await _templates.Received(1).InsertAsync(
            Arg.Is<NotificationTemplate>(t =>
                t.Code == NotificationTemplates.BookingReminder && !t.EmailEnabled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_code_nothing_renders_cannot_be_configured()
    {
        var response = await Create().UpdateTemplateAsync(
            "payment.refunded",
            new UpdateNotificationTemplateDto { SmsEnabled = true, EmailEnabled = true });

        response.IsSuccess.ShouldBeFalse();
        response.ErrorCode.ShouldBe(NotificationErrors.TemplateNotFound);

        await _templates.DidNotReceive().InsertAsync(
            Arg.Any<NotificationTemplate>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // What became of each message
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_message_names_the_order_it_was_about()
    {
        // Which is the only way to answer "the customer says they were never
        // told about WH-2609-00042".
        GivenTheOutboxHolds(Message(OutboxStatus.Failed));

        var response = await Create().SearchMessagesAsync(new NotificationMessageQueryDto());

        var row = response.Data!.Items.Single();

        row.Reference.ShouldBe("WH-2609-00042");
        row.TemplateName.ShouldBe("Order confirmation");
        row.LastError.ShouldBe("The SMS gateway did not accept the message.");
        row.CanResend.ShouldBeTrue();
    }

    [Fact]
    public async Task The_recipient_is_masked()
    {
        // A list of failures is read over somebody's shoulder. It should say
        // which customer without printing their number.
        GivenTheOutboxHolds(Message(OutboxStatus.Failed));

        var response = await Create().SearchMessagesAsync(new NotificationMessageQueryDto());

        var recipient = response.Data!.Items.Single().Recipient;

        recipient.ShouldNotBeNull();
        recipient.ShouldNotBe("+8801712349999");
    }

    [Fact]
    public async Task A_message_still_queued_cannot_be_resent()
    {
        var message = Message(OutboxStatus.Pending);

        _outbox.GetForResendAsync(1, Arg.Any<CancellationToken>()).Returns(message);

        var response = await Create().ResendAsync(1);

        response.IsSuccess.ShouldBeFalse();
        response.ErrorCode.ShouldBe(NotificationErrors.NotResendable);
        message.AttemptCount.ShouldBe(4);
    }

    [Fact]
    public async Task Nor_can_one_a_worker_is_holding()
    {
        // Resetting it races the worker into sending the same SMS twice.
        _outbox.GetForResendAsync(1, Arg.Any<CancellationToken>())
            .Returns(Message(OutboxStatus.Processing));

        var response = await Create().ResendAsync(1);

        response.ErrorCode.ShouldBe(NotificationErrors.NotResendable);
    }

    [Fact]
    public async Task A_message_that_gave_up_gets_a_full_set_of_attempts_again()
    {
        var message = Message(OutboxStatus.Failed);

        _outbox.GetForResendAsync(1, Arg.Any<CancellationToken>()).Returns(message);

        var response = await Create().ResendAsync(1);

        response.IsSuccess.ShouldBeTrue();
        message.Status.ShouldBe(OutboxStatus.Pending);
        message.AttemptCount.ShouldBe(0);
        message.NextAttemptAt.ShouldBe(_clock.UtcNow);
        message.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task Resending_a_reminder_lifts_the_hold_that_was_on_it()
    {
        // A reminder was held until the day before an appointment that has
        // since passed. Somebody pressing "send again" means now, not then.
        var message = Message(OutboxStatus.Suppressed);
        message.NotBefore = _clock.UtcNow.AddDays(-3);

        _outbox.GetForResendAsync(1, Arg.Any<CancellationToken>()).Returns(message);

        await Create().ResendAsync(1);

        message.NotBefore.ShouldBeNull();
    }

    [Fact]
    public async Task A_message_that_is_not_there_is_a_not_found()
    {
        _outbox.GetForResendAsync(9, Arg.Any<CancellationToken>()).Returns((OutboxMessage?)null);

        var response = await Create().ResendAsync(9);

        response.ErrorCode.ShouldBe(NotificationErrors.MessageNotFound);
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private void GivenTheOutboxHolds(params OutboxMessage[] messages) =>
        _outbox.SearchAsync(Arg.Any<OutboxSearch>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<OutboxMessage>(messages, messages.Length, 1, 20));

    private static OutboxMessage Message(OutboxStatus status) =>
        new()
        {
            Id = 1,
            Type = NotificationTemplates.OrderPlaced,
            IdempotencyKey = "order.placed:WH-2609-00042",
            Payload = """
                {"orderNumber":"WH-2609-00042","contactName":"Rafiqul Islam",
                 "contactPhone":"+8801712349999","language":"en","grandTotal":24500,
                 "itemCount":2,"paymentMethod":"cod","address":"Dhanmondi, Dhaka"}
                """,
            Status = status,
            AttemptCount = 4,
            LastError = "The SMS gateway did not accept the message."
        };
}
