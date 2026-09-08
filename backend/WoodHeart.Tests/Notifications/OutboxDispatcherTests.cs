using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Domain.Settings;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Notifications;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Notifications;

/// <summary>
/// Draining the outbox.
/// </summary>
/// <remarks>
/// <para>
/// The behaviours that cost money when they are wrong: <b>a failure retries
/// with a growing gap rather than immediately</b>, <b>retries stop</b>, and
/// <b>a message nothing can render is suppressed rather than attempted five
/// times</b>. Each of those is a line on a gateway invoice.
/// </para>
/// <para>
/// And the one that costs a customer: <b>an email that fails must not cause the
/// SMS to be sent again.</b> Email is the secondary channel here and most
/// orders have no address at all, so letting it decide the outcome would mean
/// re-texting every customer whose mail server was slow.
/// </para>
/// </remarks>
public class OutboxDispatcherTests
{
    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
    private readonly ISmsSender _sms = Substitute.For<ISmsSender>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();

    private readonly OutboxSettings _options = new()
    {
        BatchSize = 25,
        MaxAttempts = 3,
        RetryBaseSeconds = 60,
        RetryMaxSeconds = 3600
    };

    public OutboxDispatcherTests()
    {
        _settings.GetStringAsync(SettingKeys.StorePhone, Arg.Any<CancellationToken>())
            .Returns("01712345678");

        _sms.SendAsync(Arg.Any<PhoneNumber>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<SmsDeliveryReceipt>.Success(new SmsDeliveryReceipt("gw-1", 1, null)));

        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<string>.Success("mail-1"));

        // The real unit of work runs the delegate inside a transaction; the
        // substitute just runs it, so these tests exercise the body rather than
        // EF's transaction handling.
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<IReadOnlyList<OutboxMessage>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<IReadOnlyList<OutboxMessage>>>>()(
                    CancellationToken.None));
    }

    private OutboxDispatcher CreateDispatcher() =>
        new(_outbox,
            _sms,
            _email,
            _settings,
            _clock,
            _unitOfWork,
            Options.Create(_options),
            NullLogger<OutboxDispatcher>.Instance);

    // -------------------------------------------------------------------------
    // The happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_confirmation_is_sent_and_the_row_is_closed()
    {
        var message = GivenAMessage();

        await CreateDispatcher().RunAsync();

        await _sms.Received(1).SendAsync(
            Arg.Is<PhoneNumber>(p => p.Value == "+8801712349999"),
            Arg.Is<string>(text => text.Contains("WH-2609-00042")),
            Arg.Any<CancellationToken>());

        message.Status.ShouldBe(OutboxStatus.Processed);
        message.ProcessedAt.ShouldBe(_clock.UtcNow);
        message.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task An_empty_queue_costs_one_query_and_nothing_else()
    {
        _outbox.ClaimDueBatchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OutboxMessage>());

        await CreateDispatcher().RunAsync();

        // No shop-phone lookup, no gateway call. This runs every minute
        // forever, so what it does when there is nothing to do matters.
        await _settings.DidNotReceive().GetStringAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        await _sms.DidNotReceive().SendAsync(
            Arg.Any<PhoneNumber>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_order_with_an_email_address_gets_both()
    {
        GivenAMessage(email: "rakib@example.com");

        await CreateDispatcher().RunAsync();

        await _sms.Received(1).SendAsync(
            Arg.Any<PhoneNumber>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To == "rakib@example.com"), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_gateway_failure_is_retried_later_rather_than_immediately()
    {
        var message = GivenAMessage(attemptCount: 1);
        GivenTheGatewayIsDown();

        await CreateDispatcher().RunAsync();

        message.Status.ShouldBe(OutboxStatus.Pending);
        message.NextAttemptAt.ShouldBe(_clock.UtcNow.AddSeconds(60));
        message.LastError.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_gap_grows_with_each_attempt()
    {
        // A gateway that is down stays down for a while. Hammering it every
        // minute achieves nothing and, on a provider that bills for rejected
        // requests, achieves it expensively.
        var second = GivenAMessage(attemptCount: 2);
        GivenTheGatewayIsDown();

        await CreateDispatcher().RunAsync();

        second.NextAttemptAt.ShouldBe(_clock.UtcNow.AddSeconds(120));
    }

    [Fact]
    public async Task The_gap_is_capped()
    {
        _options.MaxAttempts = 20;
        var message = GivenAMessage(attemptCount: 15);
        GivenTheGatewayIsDown();

        await CreateDispatcher().RunAsync();

        // Uncapped, attempt fifteen would be nine days out. A message that is
        // going to arrive should arrive within the hour.
        message.NextAttemptAt.ShouldBe(_clock.UtcNow.AddSeconds(_options.RetryMaxSeconds));
    }

    [Fact]
    public async Task Retries_stop()
    {
        var message = GivenAMessage(attemptCount: 3);
        GivenTheGatewayIsDown();

        await CreateDispatcher().RunAsync();

        message.Status.ShouldBe(OutboxStatus.Failed);
        message.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_failed_email_does_not_cause_the_SMS_to_be_sent_again()
    {
        // The rule that protects the customer's phone. Email is best-effort:
        // most orders here have no address at all, and a slow mail server must
        // not turn one confirmation into five.
        var message = GivenAMessage(email: "rakib@example.com");

        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<string>.Fail(
                NotificationErrors.EmailUnavailable, "The mail server refused the message."));

        await CreateDispatcher().RunAsync();

        message.Status.ShouldBe(OutboxStatus.Processed);
    }

    // -------------------------------------------------------------------------
    // Suppression — the cases that must not be retried
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_type_with_no_template_is_suppressed_rather_than_attempted_five_times()
    {
        var message = GivenAMessage(type: "consultation.reminder");

        await CreateDispatcher().RunAsync();

        message.Status.ShouldBe(OutboxStatus.Suppressed);

        await _sms.DidNotReceive().SendAsync(
            Arg.Any<PhoneNumber>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_payload_that_cannot_be_parsed_is_suppressed()
    {
        // A row written by an older version of the code. It will not start
        // parsing on the fourth attempt.
        var message = GivenAMessage(payload: "not json at all");

        await CreateDispatcher().RunAsync();

        message.Status.ShouldBe(OutboxStatus.Suppressed);
    }

    [Fact]
    public async Task An_internal_status_change_is_suppressed_without_a_gateway_call()
    {
        // ReadyToShip is the shop talking to itself. The row exists because
        // something enqueued it; the right outcome is silence, recorded.
        var message = GivenAMessage(
            type: NotificationTemplates.OrderStatusChanged,
            payload: StatusPayload("ReadyToShip"));

        await CreateDispatcher().RunAsync();

        message.Status.ShouldBe(OutboxStatus.Suppressed);

        await _sms.DidNotReceive().SendAsync(
            Arg.Any<PhoneNumber>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_message_with_no_usable_phone_and_no_email_is_suppressed()
    {
        var message = GivenAMessage(phone: "not a number");

        await CreateDispatcher().RunAsync();

        message.Status.ShouldBe(OutboxStatus.Suppressed);
    }

    [Fact]
    public async Task But_an_unusable_phone_with_a_working_email_still_counts_as_delivered()
    {
        var message = GivenAMessage(phone: "not a number", email: "rakib@example.com");

        await CreateDispatcher().RunAsync();

        message.Status.ShouldBe(OutboxStatus.Processed);

        await _email.Received(1).SendAsync(
            Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // The reaper
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Messages_stranded_by_a_stopped_worker_are_returned_to_the_queue()
    {
        // Without this a deploy loses notifications permanently: the claim
        // query only looks at Pending rows, so anything left in Processing by a
        // killed worker is never looked at again.
        _outbox.ClaimDueBatchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OutboxMessage>());

        _outbox.ReclaimStaleAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(2);

        await CreateDispatcher().RunAsync();

        await _outbox.Received(1).ReclaimStaleAsync(
            Arg.Is<DateTimeOffset>(cutoff => cutoff < _clock.UtcNow), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private void GivenTheGatewayIsDown() =>
        _sms.SendAsync(Arg.Any<PhoneNumber>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<SmsDeliveryReceipt>.Fail(
                NotificationErrors.SmsUnavailable, "The SMS gateway could not be reached."));

    private OutboxMessage GivenAMessage(
        string type = NotificationTemplates.OrderPlaced,
        string? payload = null,
        int attemptCount = 1,
        string phone = "+8801712349999",
        string? email = null)
    {
        var message = new OutboxMessage
        {
            Id = 1,
            Type = type,
            Payload = payload ?? PlacedPayload(phone, email),
            Status = OutboxStatus.Processing,
            AttemptCount = attemptCount
        };

        _outbox.ClaimDueBatchAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([message]);

        return message;
    }

    private static string PlacedPayload(string phone, string? email) =>
        JsonSerializer.Serialize(new
        {
            orderNumber = "WH-2609-00042",
            contactName = "Rakib Hasan",
            contactPhone = phone,
            contactEmail = email,
            language = "en",
            grandTotal = 254100m,
            currency = "BDT",
            paymentMethod = "cod",
            itemCount = 3,
            address = "House 12, Road 3, Dhanmondi, Dhaka"
        });

    private static string StatusPayload(string status) =>
        JsonSerializer.Serialize(new
        {
            orderNumber = "WH-2609-00042",
            status,
            contactName = "Rakib Hasan",
            contactPhone = "+8801712349999",
            contactEmail = (string?)null,
            language = "en",
            grandTotal = 254100m,
            currency = "BDT",
            paymentStatus = "Unpaid"
        });
}
