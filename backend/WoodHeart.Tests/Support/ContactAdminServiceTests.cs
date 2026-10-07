using NSubstitute;
using Shouldly;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Support;
using WoodHeart.Domain.Enums.Support;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Support;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Support;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Services.Support;

namespace WoodHeart.Tests.Support;

/// <summary>
/// The shop's inbox: what staff see, and what reading something does to it.
/// </summary>
public class ContactAdminServiceTests
{
    private static readonly DateTimeOffset Monday =
        new(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(6));

    private readonly IContactMessageRepository _messages =
        Substitute.For<IContactMessageRepository>();

    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();

    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public ContactAdminServiceTests()
    {
        _clock.UtcNow.Returns(Monday);
        _currentUser.UserId.Returns(7L);
    }

    private ContactAdminService CreateService() =>
        new(_messages, _currentUser, _clock, _unitOfWork);

    private ContactMessage Inbox(ContactMessage message)
    {
        _messages.GetByIdAsync(message.Id, Arg.Any<CancellationToken>()).Returns(message);
        return message;
    }

    private static ContactMessage AMessage(
        ContactMessageStatus status = ContactMessageStatus.New,
        string text = "Do you make a wardrobe in mahogany?") => new()
    {
        Id = 1,
        Name = "Rafiq",
        Phone = "+8801712345678",
        Message = text,
        Status = status,
        CreatedAt = Monday
    };

    // -------------------------------------------------------------------------
    // Reading it is what changes it
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Opening_a_new_message_marks_it_read()
    {
        var message = Inbox(AMessage());

        await CreateService().GetAsync(1);

        // Asking staff to press a button to say they have read what is on the
        // screen in front of them produces a count nobody believes.
        message.Status.ShouldBe(ContactMessageStatus.Read);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Opening_an_answered_message_does_not_reopen_it()
    {
        var message = Inbox(AMessage(ContactMessageStatus.Answered));

        await CreateService().GetAsync(1);

        // Somebody checking what was said to a customer last week has not
        // un-answered them.
        message.Status.ShouldBe(ContactMessageStatus.Answered);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_id_that_is_not_in_the_inbox_is_a_not_found()
    {
        _messages.GetByIdAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns((ContactMessage?)null);

        var result = await CreateService().GetAsync(404);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(SupportErrors.MessageNotFound);
    }

    // -------------------------------------------------------------------------
    // Answering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Marking_it_answered_records_when_and_by_whom()
    {
        var message = Inbox(AMessage(ContactMessageStatus.Read));

        await CreateService().UpdateAsync(1, new UpdateContactMessageDto
        {
            Status = ContactMessageStatus.Answered
        });

        message.AnsweredAt.ShouldBe(Monday);
        message.AnsweredBy.ShouldBe(7L);
    }

    [Fact]
    public async Task Editing_a_note_afterwards_does_not_move_the_answered_date()
    {
        var answeredOnFriday = Monday.AddDays(-3);

        var message = Inbox(AMessage(ContactMessageStatus.Answered));
        message.AnsweredAt = answeredOnFriday;
        message.AnsweredBy = 3L;

        await CreateService().UpdateAsync(1, new UpdateContactMessageDto
        {
            Status = ContactMessageStatus.Answered,
            StaffNote = "Rang back, wants the 6ft."
        });

        // "How long does the shop take to reply" is a question worth being able
        // to ask, and it cannot be asked of a date that moves every time
        // somebody tidies a note.
        message.AnsweredAt.ShouldBe(answeredOnFriday);
        message.AnsweredBy.ShouldBe(3L);
        message.StaffNote.ShouldBe("Rang back, wants the 6ft.");
    }

    [Fact]
    public async Task Putting_it_back_in_the_pile_clears_the_answered_date()
    {
        var message = Inbox(AMessage(ContactMessageStatus.Answered));
        message.AnsweredAt = Monday.AddDays(-1);
        message.AnsweredBy = 3L;

        await CreateService().UpdateAsync(1, new UpdateContactMessageDto
        {
            Status = ContactMessageStatus.Read
        });

        // Otherwise it reads as answered on a day when it demonstrably was not.
        message.AnsweredAt.ShouldBeNull();
        message.AnsweredBy.ShouldBeNull();
    }

    [Fact]
    public async Task A_note_of_only_spaces_is_stored_as_nothing()
    {
        var message = Inbox(AMessage(ContactMessageStatus.Read));

        await CreateService().UpdateAsync(1, new UpdateContactMessageDto
        {
            Status = ContactMessageStatus.Read,
            StaffNote = "   "
        });

        message.StaffNote.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // The list
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_list_masks_the_number_and_the_message_does_not()
    {
        Page(AMessage());
        Inbox(AMessage(ContactMessageStatus.Read));

        var listed = (await CreateService().SearchAsync(new ContactMessageQueryDto())).Data!.Items[0];
        var opened = (await CreateService().GetAsync(1)).Data!;

        // The list is read over somebody's shoulder in an office; the message
        // is the screen they ring from, and a number nobody can dial is not a
        // contact.
        listed.Phone.ShouldBe("017****5678");
        opened.Phone.ShouldBe("+8801712345678");
    }

    [Fact]
    public async Task A_long_message_is_cut_to_a_preview()
    {
        Page(AMessage(text: new string('x', 400)));

        var listed = (await CreateService().SearchAsync(new ContactMessageQueryDto())).Data!.Items[0];

        listed.Preview.Length.ShouldBeLessThan(200);
        listed.Preview.ShouldEndWith("…");
    }

    [Fact]
    public async Task A_message_written_across_several_lines_previews_as_one()
    {
        Page(AMessage(text: "Hello.\n\n   I need a bed.\n\nThanks"));

        var listed = (await CreateService().SearchAsync(new ContactMessageQueryDto())).Data!.Items[0];

        // A list row is one line high. Newlines in it either vanish and run the
        // words together or stretch the row to four.
        listed.Preview.ShouldBe("Hello. I need a bed. Thanks");
    }

    [Fact]
    public async Task The_filters_reach_the_query()
    {
        ContactMessageQuery? sent = null;

        _messages.SearchAsync(Arg.Do<ContactMessageQuery>(q => sent = q), Arg.Any<CancellationToken>())
            .Returns(new PagedList<ContactMessage>([], 0, 1, 20));

        await CreateService().SearchAsync(new ContactMessageQueryDto
        {
            Status = ContactMessageStatus.New,
            Topic = ContactTopic.Complaint,
            Term = "01712345678",
            Page = 2,
            PageSize = 50
        });

        sent.ShouldNotBeNull();
        sent!.Status.ShouldBe(ContactMessageStatus.New);
        sent.Topic.ShouldBe(ContactTopic.Complaint);
        sent.Term.ShouldBe("01712345678");
        sent.PageNumber.ShouldBe(2);
        sent.PageSize.ShouldBe(50);
    }

    [Fact]
    public async Task The_badge_counts_what_nobody_has_opened()
    {
        _messages.CountNewAsync(Arg.Any<CancellationToken>()).Returns(4);

        (await CreateService().CountNewAsync()).Data.ShouldBe(4);
    }

    private void Page(params ContactMessage[] messages) =>
        _messages.SearchAsync(Arg.Any<ContactMessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<ContactMessage>(messages, messages.Length, 1, 20));
}
