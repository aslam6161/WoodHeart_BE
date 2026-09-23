using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Consultations;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Consultations;
using WoodHeart.Service.Mapping.Consultations;

namespace WoodHeart.Service.Services.Consultations;

/// <summary>
/// Writing what the shop offers and who is free when.
/// </summary>
/// <remarks>
/// <para>
/// <b>A consultant's diary is replaced wholesale, not edited row by row.</b> A
/// week is one decision — the Friday off only means anything beside the
/// Saturday morning — and a partial save is how somebody ends up bookable on a
/// day nobody meant. The same all-or-nothing shape as the settings screen, and
/// for the same reason.
/// </para>
/// <para>
/// Nothing here decides what is free. That is <c>SlotGenerator</c>'s, and
/// keeping the two apart is what makes the generator exhaustively testable.
/// </para>
/// </remarks>
public class ConsultationAdminService(
    IConsultationServiceRepository services,
    IConsultantRepository consultants,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    ILogger<ConsultationAdminService> logger) : IConsultationAdminService
{
    public async Task<GeneralResponse<IReadOnlyList<ConsultationServiceDto>>> GetServicesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await services.GetAllForAdminAsync(cancellationToken);

        return GeneralResponse<IReadOnlyList<ConsultationServiceDto>>.Success(
            [.. rows.Select(row => ConsultationMapper.ToDto(row, currentUser.Language))]);
    }

    public async Task<GeneralResponse<ConsultationServiceDto>> SaveServiceAsync(
        long? id, SaveConsultationServiceDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.DurationMinutes <= 0)
        {
            return ServiceFail(ConsultationErrors.DurationInvalid, "An appointment has to last some minutes.");
        }

        // Derived from the name when nobody typed one, exactly as a product's
        // is: a slug is a URL, and asking a shop owner to invent one is asking
        // for a URL with a space in it.
        var slug = Slug.From(
            string.IsNullOrWhiteSpace(dto.Slug) ? dto.NameEn : dto.Slug);

        if (await services.SlugExistsAsync(slug.Value, id, cancellationToken))
        {
            return ServiceFail(
                ConsultationErrors.SlugTaken, $"Another consultation already uses the address {slug.Value}.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var service = id is { } existingId
                ? await services.GetWithConsultantsAsync(existingId, ct)
                : new ConsultationService();

            if (service is null)
            {
                return ServiceFail(ConsultationErrors.ServiceNotFound, "That consultation no longer exists.");
            }

            service.Name = LocalizedText.Create(dto.NameEn.Trim(), Blank(dto.NameBn));
            service.Slug = slug;
            service.Description = string.IsNullOrWhiteSpace(dto.DescriptionEn)
                ? null
                : LocalizedText.Create(dto.DescriptionEn.Trim(), Blank(dto.DescriptionBn));
            service.Mode = dto.Mode;
            service.DurationMinutes = dto.DurationMinutes;
            service.Fee = Money.Taka(dto.Fee);
            service.RequiresAdvance = dto.RequiresAdvance;
            service.AdvanceAmount = dto.AdvanceAmount is { } advance ? Money.Taka(advance) : null;
            service.BufferBeforeMinutes = dto.BufferBeforeMinutes;
            service.BufferAfterMinutes = dto.BufferAfterMinutes;
            service.IsActive = dto.IsActive;
            service.SortOrder = dto.SortOrder;

            if (id is null)
            {
                await services.InsertAsync(service, ct);
            }
            else
            {
                services.Update(service);
            }

            await unitOfWork.SaveChangesAsync(ct);

            return GeneralResponse<ConsultationServiceDto>.Success(
                ConsultationMapper.ToDto(service, currentUser.Language), id: service.Id);
        }, cancellationToken);
    }

    public async Task<GeneralResponse<IReadOnlyList<ConsultantDto>>> GetConsultantsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await consultants.GetAllForAdminAsync(cancellationToken);

        return GeneralResponse<IReadOnlyList<ConsultantDto>>.Success(
            [.. rows.Select(row => ConsultationMapper.ToDto(row, currentUser.Language))]);
    }

    public async Task<GeneralResponse<ConsultantScheduleDto>> GetConsultantAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var consultant = await consultants.GetWithScheduleAsync(id, cancellationToken);

        return consultant is null
            ? ConsultantFail(ConsultationErrors.ConsultantNotFound, "That consultant no longer exists.")
            : GeneralResponse<ConsultantScheduleDto>.Success(
                ConsultationMapper.ToScheduleDto(consultant, currentUser.Language));
    }

    public async Task<GeneralResponse<ConsultantScheduleDto>> SaveConsultantAsync(
        long? id, SaveConsultantDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return ConsultantFail(ConsultationErrors.NameRequired, "A consultant needs a name.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var consultant = id is { } existingId
                ? await consultants.GetWithScheduleAsync(existingId, ct)
                : new Consultant();

            if (consultant is null)
            {
                return ConsultantFail(
                    ConsultationErrors.ConsultantNotFound, "That consultant no longer exists.");
            }

            consultant.Name = dto.Name.Trim();
            consultant.PhotoPath = Blank(dto.PhotoPath);
            consultant.Bio = string.IsNullOrWhiteSpace(dto.BioEn)
                ? null
                : LocalizedText.Create(dto.BioEn.Trim(), Blank(dto.BioBn));
            consultant.Specialities =
            [
                .. dto.Specialities
                    .Where(speciality => !string.IsNullOrWhiteSpace(speciality))
                    .Select(speciality => speciality.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
            consultant.IsActive = dto.IsActive;
            consultant.SortOrder = dto.SortOrder;

            // Replaced wholesale. The form sends the complete set, the same
            // way the discount form sends its targets, because a partial
            // update of a list is the shape that quietly leaves one behind.
            consultant.Services.Clear();

            foreach (var serviceId in dto.ServiceIds.Distinct())
            {
                consultant.Services.Add(new ConsultantService { ConsultationServiceId = serviceId });
            }

            if (id is null)
            {
                await consultants.InsertAsync(consultant, ct);
            }
            else
            {
                consultants.Update(consultant);
            }

            await unitOfWork.SaveChangesAsync(ct);

            return GeneralResponse<ConsultantScheduleDto>.Success(
                ConsultationMapper.ToScheduleDto(consultant, currentUser.Language), id: consultant.Id);
        }, cancellationToken);
    }

    public async Task<GeneralResponse<ConsultantScheduleDto>> SaveScheduleAsync(
        long consultantId, SaveScheduleDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        foreach (var rule in dto.Rules)
        {
            if (rule.EndTime <= rule.StartTime)
            {
                return ConsultantFail(
                    ConsultationErrors.WindowInvalid,
                    $"The {rule.DayOfWeek} window has to end after it starts.");
            }

            if (rule.SlotMinutes <= 0)
            {
                return ConsultantFail(
                    ConsultationErrors.DurationInvalid, "A slot has to be some minutes long.");
            }
        }

        foreach (var exception in dto.Exceptions.Where(entry => !entry.IsClosed))
        {
            if (exception.StartTime is not { } start || exception.EndTime is not { } end || end <= start)
            {
                return ConsultantFail(
                    ConsultationErrors.WindowInvalid,
                    $"The window on {exception.Date:d MMM} has to end after it starts, "
                    + "or the day has to be marked closed.");
            }
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var consultant = await consultants.GetWithScheduleAsync(consultantId, ct);

            if (consultant is null)
            {
                return ConsultantFail(
                    ConsultationErrors.ConsultantNotFound, "That consultant no longer exists.");
            }

            consultant.AvailabilityRules.Clear();
            consultant.AvailabilityExceptions.Clear();

            foreach (var rule in dto.Rules)
            {
                consultant.AvailabilityRules.Add(new AvailabilityRule
                {
                    DayOfWeek = rule.DayOfWeek,
                    StartTime = rule.StartTime,
                    EndTime = rule.EndTime,
                    SlotMinutes = rule.SlotMinutes
                });
            }

            foreach (var entry in dto.Exceptions.DistinctBy(entry => entry.Date))
            {
                consultant.AvailabilityExceptions.Add(new AvailabilityException
                {
                    Date = entry.Date,
                    IsClosed = entry.IsClosed,
                    StartTime = entry.IsClosed ? null : entry.StartTime,
                    EndTime = entry.IsClosed ? null : entry.EndTime,
                    Note = Blank(entry.Note)
                });
            }

            consultants.Update(consultant);
            await unitOfWork.SaveChangesAsync(ct);

            ConsultationLog.ScheduleSaved(
                logger, consultant.Name, dto.Rules.Count, dto.Exceptions.Count);

            return GeneralResponse<ConsultantScheduleDto>.Success(
                ConsultationMapper.ToScheduleDto(consultant, currentUser.Language));
        }, cancellationToken);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static GeneralResponse<ConsultationServiceDto> ServiceFail(string code, string message) =>
        GeneralResponse<ConsultationServiceDto>.Fail(code, message);

    private static GeneralResponse<ConsultantScheduleDto> ConsultantFail(string code, string message) =>
        GeneralResponse<ConsultantScheduleDto>.Fail(code, message);
}
