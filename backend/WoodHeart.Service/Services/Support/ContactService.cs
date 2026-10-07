using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Support;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Support;
using WoodHeart.Service.DTOs.Support;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Support;

namespace WoodHeart.Service.Services.Support;

/// <inheritdoc cref="IContactService" />
/// <remarks>
/// <para>
/// <b>The page is drawn from the shop's own settings.</b> Phone, email, address
/// and opening hours are rows an admin can change at two in the afternoon, so
/// moving showroom does not need a deployment — and anything still blank is
/// returned as null and left off the page rather than printed as a dash.
/// </para>
/// <para>
/// <b>A message is a row before it is anything else.</b> Nothing here sends an
/// email or an SMS, which is the point: the shop's inbox is in the same
/// application and the same database as the rest of the admin, so a message
/// cannot be lost to an SMTP password that expired last month. If the shop
/// later wants a notification when one arrives, that is a separate thing that
/// can fail without taking the message with it.
/// </para>
/// </remarks>
public class ContactService(
    IContactMessageRepository messages,
    IStoreSettingService settings,
    IUnitOfWork unitOfWork) : IContactService
{
    public async Task<GeneralResponse<ContactDetailsDto>> GetDetailsAsync(
        CancellationToken cancellationToken = default)
    {
        var phone = Clean(await settings.GetStringAsync(SettingKeys.StorePhone, cancellationToken));

        // Two forms of the same number: one a person reads, one a phone dials.
        // Storing only the second would print +8801712345678 on a page meant to
        // look like a local shop, and only the first makes a broken tel: link.
        var parsed = PhoneNumber.TryParse(phone, out var number) ? number : null;

        return GeneralResponse<ContactDetailsDto>.Success(new ContactDetailsDto
        {
            ShopName = Clean(await settings.GetStringAsync(SettingKeys.StoreName, cancellationToken)),
            Phone = parsed?.National ?? phone,
            PhoneE164 = parsed?.Value,
            Email = Clean(await settings.GetStringAsync(SettingKeys.StoreEmail, cancellationToken)),
            Address = Clean(await settings.GetStringAsync(SettingKeys.StoreAddress, cancellationToken)),
            Hours = Clean(await settings.GetStringAsync(SettingKeys.StoreHours, cancellationToken))
        });
    }

    public async Task<GeneralResponse<ContactReceiptDto>> SubmitAsync(
        SubmitContactMessageDto dto, CancellationToken cancellationToken = default)
    {
        var phone = Clean(dto.Phone);
        var email = Clean(dto.Email)?.ToLowerInvariant();

        if (phone is null && email is null)
        {
            // Neither field is required on its own, so this cannot be said with
            // [Required] — and "this field is required" under both of them
            // would be wrong twice.
            return GeneralResponse<ContactReceiptDto>.Fail(
                SupportErrors.NoReplyRoute,
                "Leave a mobile number or an email address so we can reply.");
        }

        if (phone is not null)
        {
            if (!PhoneNumber.TryParse(phone, out var number) || number is null)
            {
                return GeneralResponse<ContactReceiptDto>.Fail(
                    PhoneNumber.InvalidCode, PhoneNumber.InvalidMessage);
            }

            // Normalised, so the same customer writing twice from two spellings
            // of their number is one searchable string in the inbox.
            phone = number.Value;
        }

        await messages.InsertAsync(new ContactMessage
        {
            Name = dto.Name.Trim(),
            Phone = phone,
            Email = email,
            Topic = dto.Topic,
            Reference = Clean(dto.Reference),
            Message = dto.Message.Trim()
        }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<ContactReceiptDto>.Success(new ContactReceiptDto
        {
            // Said back to them, because the commonest fault with a contact form
            // is a mistyped address nobody notices until the reply never comes.
            ReplyTo = phone is not null
                ? PhoneNumber.TryParse(phone, out var reply) && reply is not null
                    ? reply.National
                    : phone
                : email!,
            Hours = Clean(await settings.GetStringAsync(SettingKeys.StoreHours, cancellationToken))
        });
    }

    /// <summary>Blank and whitespace both mean "not set", and neither belongs on a page.</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
