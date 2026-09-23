using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Interfaces.Consultations;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// Consultations: what is offered, when it can happen, and booking one.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AllowAnonymous]</c> is the point rather than an oversight. Somebody who
/// wants a consultant to look at their flat should not have to make an account
/// first, so a guest can browse the calendar, book, and find their booking
/// again afterwards by quoting the number and the phone it was booked with —
/// the same two facts a guest order is tracked by.
/// </para>
/// <para>
/// Booking is on the checkout rate-limiting policy rather than the public one.
/// It writes, it sends an SMS, and it takes an afternoon out of somebody's
/// diary; browsing the calendar beside it is ordinary reading.
/// </para>
/// </remarks>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/consultations")]
public class ConsultationsController(
    IAvailabilityService availability, IBookingService bookings) : BaseApiController
{
    /// <summary>Everything the shop offers an hour of.</summary>
    [HttpGet("services")]
    public async Task<IActionResult> Services(CancellationToken cancellationToken) =>
        HandleResult(await availability.GetServicesAsync(cancellationToken));

    [HttpGet("services/{slug}")]
    public async Task<IActionResult> Service(string slug, CancellationToken cancellationToken) =>
        HandleResult(await availability.GetServiceBySlugAsync(slug, cancellationToken));

    /// <summary>The people who do it, optionally narrowed to one service.</summary>
    [HttpGet("consultants")]
    public async Task<IActionResult> Consultants(
        [FromQuery] long? serviceId, CancellationToken cancellationToken) =>
        HandleResult(await availability.GetConsultantsAsync(serviceId, cancellationToken));

    /// <summary>
    /// The times a customer can actually pick, grouped by the day they see
    /// them under.
    /// </summary>
    /// <remarks>
    /// Dates are Dhaka's and the slots come back as UTC instants, because a
    /// browser in any time zone can render an instant correctly and cannot
    /// rebuild one from a local time it was not told the offset for.
    /// </remarks>
    [HttpGet("availability")]
    public async Task<IActionResult> Availability(
        [FromQuery] AvailabilityQueryDto query, CancellationToken cancellationToken) =>
        HandleResult(await availability.GetAvailabilityAsync(query, cancellationToken));

    /// <summary>
    /// Books a slot.
    /// </summary>
    /// <remarks>
    /// The time is re-checked here against the same schedule the calendar was
    /// drawn from, so a request naming a time that was never offered is
    /// refused however it was constructed. Send an <c>Idempotency-Key</c>: a
    /// double-tap on a slow connection must not block two afternoons.
    /// </remarks>
    [EnableRateLimiting(RateLimitPolicies.Checkout)]
    [HttpPost("bookings")]
    public async Task<IActionResult> Book(
        [FromBody] CreateBookingDto dto,
        [FromHeader(Name = GlobalConstants.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        HandleResult(await bookings.CreateAsync(dto, idempotencyKey, cancellationToken));

    /// <summary>
    /// One booking, for whoever owns it.
    /// </summary>
    /// <remarks>
    /// A signed-in customer needs nothing else. A guest quotes the phone
    /// number it was booked with, so a guessed booking number on its own
    /// discloses nothing.
    /// </remarks>
    [HttpGet("bookings/{bookingNumber}")]
    public async Task<IActionResult> Booking(
        string bookingNumber,
        [FromQuery] string? phone,
        CancellationToken cancellationToken) =>
        HandleResult(await bookings.GetAsync(bookingNumber, phone, cancellationToken));

    /// <summary>The customer calling it off themselves, while they still may.</summary>
    [EnableRateLimiting(RateLimitPolicies.Checkout)]
    [HttpPost("bookings/{bookingNumber}/cancel")]
    public async Task<IActionResult> Cancel(
        string bookingNumber,
        [FromBody] CancelBookingDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(
            await bookings.CancelAsync(bookingNumber, dto?.Phone, dto?.Reason, cancellationToken));

    /// <summary>A signed-in customer's own bookings.</summary>
    [Authorize(Policy = Policies.RequireCustomer)]
    [HttpGet("my-bookings")]
    public async Task<IActionResult> MyBookings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = BookingRules.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        HandleResult(await bookings.GetMineAsync(page, pageSize, cancellationToken));
}

/// <summary>A customer calling off their own booking.</summary>
/// <remarks>
/// The phone number is here rather than in the query string for the same
/// reason a guest order lookup puts it in the body: a URL ends up in browser
/// history, in a proxy log and in a referrer header, and a phone number is the
/// other half of the credential.
/// </remarks>
public class CancelBookingDto
{
    public string? Phone { get; init; }

    /// <summary>Why. Optional, and worth asking for.</summary>
    [System.ComponentModel.DataAnnotations.StringLength(500)]
    public string? Reason { get; init; }
}
