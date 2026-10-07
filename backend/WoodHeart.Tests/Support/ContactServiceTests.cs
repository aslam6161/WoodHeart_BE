using NSubstitute;
using Shouldly;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Support;
using WoodHeart.Domain.Enums.Support;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Support;
using WoodHeart.Service.DTOs.Support;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Services.Support;

namespace WoodHeart.Tests.Support;

/// <summary>
/// The contact page, and the one promise it makes: somebody will get back to
/// you on what you left.
/// </summary>
/// <remarks>
/// Which makes the reply route the thing worth testing hardest. A message with
/// no way back is not an enquiry, a mistyped mobile number is the commonest
/// fault a contact form has, and a shop that has not filled in its address
/// should show a page without one rather than a page with a dash on it.
/// </remarks>
public class ContactServiceTests
{
    private readonly IContactMessageRepository _messages =
        Substitute.For<IContactMessageRepository>();

    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public ContactServiceTests() =>
        _settings.GetStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

    private ContactService CreateService() => new(_messages, _settings, _unitOfWork);

    private void Setting(string key, string? value) =>
        _settings.GetStringAsync(key, Arg.Any<CancellationToken>()).Returns(value);

    private static SubmitContactMessageDto Message(
        string? phone = "01712345678", string? email = null) => new()
    {
        Name = "Rafiq",
        Phone = phone,
        Email = email,
        Message = "Do you make a wardrobe in mahogany?"
    };

    // -------------------------------------------------------------------------
    // A message the shop cannot answer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_message_with_no_way_back_is_refused()
    {
        var result = await CreateService().SubmitAsync(Message(phone: null));

        // Its own code, because the form has to say one specific thing. Neither
        // field is required on its own, so "this field is required" under both
        // of them would be wrong twice.
        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(SupportErrors.NoReplyRoute);

        await _messages.DidNotReceive()
            .InsertAsync(Arg.Any<ContactMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_mistyped_mobile_number_is_refused_before_it_is_stored()
    {
        // The commonest fault a contact form has. Stored, it becomes a message
        // somebody tries to answer three times and gives up on.
        var result = await CreateService().SubmitAsync(Message(phone: "0171234"));

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(PhoneNumber.InvalidCode);

        await _messages.DidNotReceive()
            .InsertAsync(Arg.Any<ContactMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_email_on_its_own_is_enough()
    {
        var result = await CreateService().SubmitAsync(
            Message(phone: null, email: "Rafiq@Example.COM"));

        result.IsSuccess.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // What is stored
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_number_is_stored_in_one_form_however_it_was_typed()
    {
        ContactMessage? stored = null;

        await _messages.InsertAsync(
            Arg.Do<ContactMessage>(message => stored = message), Arg.Any<CancellationToken>());

        await CreateService().SubmitAsync(Message(phone: "017-1234-5678"));

        // So the same customer writing in twice is one searchable string in the
        // inbox rather than two strangers.
        stored.ShouldNotBeNull();
        stored!.Phone.ShouldBe("+8801712345678");
    }

    [Fact]
    public async Task The_email_is_stored_lower_cased()
    {
        ContactMessage? stored = null;

        await _messages.InsertAsync(
            Arg.Do<ContactMessage>(message => stored = message), Arg.Any<CancellationToken>());

        await CreateService().SubmitAsync(Message(phone: null, email: "Rafiq@Example.COM"));

        stored!.Email.ShouldBe("rafiq@example.com");
    }

    [Fact]
    public async Task A_new_message_is_unread_until_somebody_opens_it()
    {
        ContactMessage? stored = null;

        await _messages.InsertAsync(
            Arg.Do<ContactMessage>(message => stored = message), Arg.Any<CancellationToken>());

        await CreateService().SubmitAsync(Message());

        stored!.Status.ShouldBe(ContactMessageStatus.New);
    }

    [Fact]
    public async Task The_message_is_saved_rather_than_left_in_the_change_tracker()
    {
        await CreateService().SubmitAsync(Message());

        // Without this the endpoint answers "sent" and the row never lands.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_customer_is_told_where_the_reply_will_go()
    {
        var result = await CreateService().SubmitAsync(Message(phone: "01712345678"));

        // Said back in the form they typed, because a mistyped address is only
        // ever noticed when the reply does not come.
        result.Data!.ReplyTo.ShouldBe("01712345678");
    }

    // -------------------------------------------------------------------------
    // The shop's own details
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_detail_the_shop_has_not_filled_in_comes_back_as_nothing()
    {
        Setting(SettingKeys.StoreAddress, "   ");

        var details = (await CreateService().GetDetailsAsync()).Data!;

        // Null, so the page leaves the line out. "Address: —" reads as a shop
        // that failed to load rather than one that has not got round to it.
        details.Address.ShouldBeNull();
    }

    [Fact]
    public async Task The_number_comes_back_in_both_the_forms_the_page_needs()
    {
        Setting(SettingKeys.StorePhone, "01712345678");

        var details = (await CreateService().GetDetailsAsync()).Data!;

        // One for somebody to read, one for the telephone to dial. Printing
        // E.164 on the page makes a local shop look like a call centre;
        // dialling the national form from abroad fails.
        details.Phone.ShouldBe("01712345678");
        details.PhoneE164.ShouldBe("+8801712345678");
    }

    [Fact]
    public async Task A_number_the_shop_typed_wrongly_is_still_shown()
    {
        Setting(SettingKeys.StorePhone, "ask at the showroom");

        var details = (await CreateService().GetDetailsAsync()).Data!;

        // Shown as written, with no tel: link made from it. Hiding the shop's
        // own telephone number because it failed a regex is the worst outcome
        // on a page whose job is to give it out.
        details.Phone.ShouldBe("ask at the showroom");
        details.PhoneE164.ShouldBeNull();
    }
}
