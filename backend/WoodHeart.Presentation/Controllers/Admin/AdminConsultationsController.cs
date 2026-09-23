using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Interfaces.Consultations;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// The shop's side of consultations: what is offered, who is free, and moving
/// a booking along.
/// </summary>
/// <remarks>
/// Reading is for all staff — whoever answers the telephone needs to be able
/// to say when the next site visit is free. Writing the schedule is admin or
/// manager: a consultant's week is what the booking page offers, and a rule
/// typed wrong is either a day nobody can book or a day somebody is expected
/// to work and does not know it.
/// </remarks>
[Authorize(Policy = Policies.RequireStaff)]
[Route("api/admin/consultations")]
public class AdminConsultationsController(
    IConsultationAdminService schedule, IBookingService bookings) : BaseApiController
{
    // --- What is offered ------------------------------------------------------

    [HttpGet("services")]
    public async Task<IActionResult> Services(CancellationToken cancellationToken) =>
        HandleResult(await schedule.GetServicesAsync(cancellationToken));

    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPost("services")]
    public async Task<IActionResult> CreateService(
        SaveConsultationServiceDto dto, CancellationToken cancellationToken) =>
        HandleResult(await schedule.SaveServiceAsync(null, dto, cancellationToken));

    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPut("services/{id:long}")]
    public async Task<IActionResult> UpdateService(
        long id, SaveConsultationServiceDto dto, CancellationToken cancellationToken) =>
        HandleResult(await schedule.SaveServiceAsync(id, dto, cancellationToken));

    // --- Who does it ----------------------------------------------------------

    [HttpGet("consultants")]
    public async Task<IActionResult> Consultants(CancellationToken cancellationToken) =>
        HandleResult(await schedule.GetConsultantsAsync(cancellationToken));

    [HttpGet("consultants/{id:long}")]
    public async Task<IActionResult> Consultant(long id, CancellationToken cancellationToken) =>
        HandleResult(await schedule.GetConsultantAsync(id, cancellationToken));

    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPost("consultants")]
    public async Task<IActionResult> CreateConsultant(
        SaveConsultantDto dto, CancellationToken cancellationToken) =>
        HandleResult(await schedule.SaveConsultantAsync(null, dto, cancellationToken));

    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPut("consultants/{id:long}")]
    public async Task<IActionResult> UpdateConsultant(
        long id, SaveConsultantDto dto, CancellationToken cancellationToken) =>
        HandleResult(await schedule.SaveConsultantAsync(id, dto, cancellationToken));

    /// <summary>
    /// Replaces a consultant's week and their exceptions in one go.
    /// </summary>
    /// <remarks>
    /// All-or-nothing, like the settings screen. A week is one decision, and a
    /// half-saved one is a consultant bookable on a day nobody meant.
    /// </remarks>
    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPut("consultants/{id:long}/schedule")]
    public async Task<IActionResult> SaveSchedule(
        long id, SaveScheduleDto dto, CancellationToken cancellationToken) =>
        HandleResult(await schedule.SaveScheduleAsync(id, dto, cancellationToken));

    // --- A booking that exists -------------------------------------------------

    [HttpGet("bookings/{bookingNumber}")]
    public async Task<IActionResult> Booking(
        string bookingNumber, CancellationToken cancellationToken) =>
        // Staff read a booking without quoting the phone number: they are the
        // shop, and they are the ones being telephoned about it.
        HandleResult(await bookings.GetForStaffAsync(bookingNumber, cancellationToken));

    /// <summary>Confirm it, move it, complete it, or record that nobody came.</summary>
    [HttpPut("bookings/{bookingNumber}/status")]
    public async Task<IActionResult> SetStatus(
        string bookingNumber,
        ChangeBookingStatusDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(
            await bookings.SetStatusAsync(bookingNumber, dto.Status, dto.Note, cancellationToken));
}
