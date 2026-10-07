using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Support;
using WoodHeart.Domain.Enums.Support;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Support;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.DTOs.Support;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Support;

namespace WoodHeart.Service.Services.Support;

/// <inheritdoc cref="IContactAdminService" />
public class ContactAdminService(
    IContactMessageRepository messages,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork) : IContactAdminService
{
    /// <summary>How much of a message the list shows before it has to be opened.</summary>
    private const int PreviewLength = 140;

    public async Task<GeneralResponse<PagedResult<ContactMessageListItemDto>>> SearchAsync(
        ContactMessageQueryDto query, CancellationToken cancellationToken = default)
    {
        var page = await messages.SearchAsync(new ContactMessageQuery
        {
            Status = query.Status,
            Topic = query.Topic,
            Term = query.Term,
            PageNumber = query.Page,
            PageSize = query.PageSize
        }, cancellationToken);

        return GeneralResponse<PagedResult<ContactMessageListItemDto>>.Success(
            new PagedResult<ContactMessageListItemDto>
            {
                Items = [.. page.Select(ToListItem)],
                Total = page.TotalCount,
                Page = page.CurrentPage,
                PageSize = page.PageSize
            });
    }

    public async Task<GeneralResponse<ContactMessageDto>> GetAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var message = await messages.GetByIdAsync(id, cancellationToken);

        if (message is null)
        {
            return GeneralResponse<ContactMessageDto>.Fail(
                SupportErrors.MessageNotFound, "That message is not in the inbox.");
        }

        // Opening it is what marks it read. Only New moves: somebody checking
        // what was said to a customer last week has not un-answered it.
        if (message.Status == ContactMessageStatus.New)
        {
            message.Status = ContactMessageStatus.Read;
            messages.Update(message);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return GeneralResponse<ContactMessageDto>.Success(ToDetail(message));
    }

    public async Task<GeneralResponse<ContactMessageDto>> UpdateAsync(
        long id, UpdateContactMessageDto dto, CancellationToken cancellationToken = default)
    {
        var message = await messages.GetByIdAsync(id, cancellationToken);

        if (message is null)
        {
            return GeneralResponse<ContactMessageDto>.Fail(
                SupportErrors.MessageNotFound, "That message is not in the inbox.");
        }

        var wasAnswered = message.Status == ContactMessageStatus.Answered;

        message.Status = dto.Status;
        message.StaffNote = string.IsNullOrWhiteSpace(dto.StaffNote) ? null : dto.StaffNote.Trim();

        if (dto.Status == ContactMessageStatus.Answered)
        {
            // Stamped once. Re-saving a note on a message answered on Monday
            // must not make it look answered on Thursday — "how long did the
            // shop take to reply" is a question worth being able to ask.
            if (!wasAnswered)
            {
                message.AnsweredAt = clock.UtcNow;
                message.AnsweredBy = currentUser.UserId;
            }
        }
        else
        {
            message.AnsweredAt = null;
            message.AnsweredBy = null;
        }

        messages.Update(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<ContactMessageDto>.Success(ToDetail(message));
    }

    public async Task<GeneralResponse<int>> CountNewAsync(
        CancellationToken cancellationToken = default) =>
        GeneralResponse<int>.Success(await messages.CountNewAsync(cancellationToken));

    // -------------------------------------------------------------------------

    private static ContactMessageListItemDto ToListItem(ContactMessage message) => new()
    {
        Id = message.Id,
        Name = message.Name,
        // Masked in the list, whole on the message. The list is read over
        // somebody's shoulder; the message is the screen they ring from.
        Phone = Mask(message.Phone),
        Email = message.Email,
        Topic = message.Topic,
        Status = message.Status,
        Preview = Preview(message.Message),
        Reference = message.Reference,
        ReceivedAt = message.CreatedAt
    };

    private static ContactMessageDto ToDetail(ContactMessage message) => new()
    {
        Id = message.Id,
        Name = message.Name,
        Phone = message.Phone,
        Email = message.Email,
        Topic = message.Topic,
        Status = message.Status,
        Message = message.Message,
        Reference = message.Reference,
        StaffNote = message.StaffNote,
        ReceivedAt = message.CreatedAt,
        AnsweredAt = message.AnsweredAt
    };

    private static string? Mask(string? phone) =>
        PhoneNumber.TryParse(phone, out var parsed) && parsed is not null ? parsed.Masked : phone;

    private static string Preview(string message)
    {
        var flat = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return flat.Length <= PreviewLength ? flat : flat[..PreviewLength].TrimEnd() + "…";
    }
}
