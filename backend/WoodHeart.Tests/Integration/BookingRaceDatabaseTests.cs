using Microsoft.EntityFrameworkCore;
using Npgsql;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.Enums.Consultations;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Presentation.Errors;
using WoodHeart.Repository;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// Two customers, one four o'clock, against PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The slot generator's tests prove the arithmetic; this proves the guard.
/// Two connections both see the slot as free — which they will, because the
/// availability check runs before either writes — and both try to book it.
/// Without the unique index both succeed and two people arrive at the studio
/// at the same time. With it, the second write is refused by the database,
/// which is the only participant that cannot lose the race.
/// </para>
/// <para>
/// It also proves the two halves of the filter that make the index usable: a
/// cancelled booking frees its afternoon, and a consultant's four o'clock is
/// only their own.
/// </para>
/// </remarks>
public class BookingRaceDatabaseTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Slot = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [RequiresPostgresFact]
    public async Task Two_customers_cannot_both_take_one_consultant_at_one_time()
    {
        long serviceId;
        long consultantId;

        await using (var seeding = fixture.CreateContext())
        {
            (serviceId, consultantId) = await SeedAsync(seeding);
        }

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();

        first.Bookings.Add(Booking(serviceId, consultantId, "WHC-2609-00001", Slot));
        second.Bookings.Add(Booking(serviceId, consultantId, "WHC-2609-00002", Slot));

        await first.SaveChangesAsync();

        var failure = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());

        // The constraint by name, because the middleware maps it by name into
        // "somebody booked that time while you were filling in the form".
        var postgres = failure.InnerException.ShouldBeOfType<PostgresException>();

        postgres.SqlState.ShouldBe("23505");
        postgres.ConstraintName.ShouldBe("ux_bookings_consultant_slot");

        UniqueViolation.Describe(failure)!.Value.Code.ShouldBe(
            Domain.Constants.ConsultationErrors.SlotTaken);
    }

    [RequiresPostgresFact]
    public async Task A_cancelled_booking_frees_its_afternoon()
    {
        long serviceId;
        long consultantId;

        await using var context = fixture.CreateContext();

        (serviceId, consultantId) = await SeedAsync(context);

        var cancelled = Booking(serviceId, consultantId, "WHC-2609-00003", Slot);
        cancelled.Status = BookingStatus.Cancelled;

        context.Bookings.Add(cancelled);
        await context.SaveChangesAsync();

        // The index is filtered to the statuses that still hold the slot, so
        // this is allowed — and has to be, or an afternoon somebody called off
        // could never be sold again.
        context.Bookings.Add(Booking(serviceId, consultantId, "WHC-2609-00004", Slot));

        await Should.NotThrowAsync(() => context.SaveChangesAsync());
    }

    [RequiresPostgresFact]
    public async Task Two_consultants_may_both_be_busy_at_four_o_clock()
    {
        await using var context = fixture.CreateContext();

        var (serviceId, first) = await SeedAsync(context);
        var second = await SeedConsultantAsync(context, serviceId, "Nadia");

        context.Bookings.Add(Booking(serviceId, first, "WHC-2609-00005", Slot));
        context.Bookings.Add(Booking(serviceId, second, "WHC-2609-00006", Slot));

        await Should.NotThrowAsync(() => context.SaveChangesAsync());
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static async Task<(long ServiceId, long ConsultantId)> SeedAsync(DataContext context)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var service = new ConsultationService
        {
            Name = LocalizedText.Create("Studio consultation"),
            Slug = Slug.From($"studio-{suffix}"),
            Mode = ConsultationMode.InStudio,
            DurationMinutes = 60,
            Fee = Money.Taka(2_000m),
            IsActive = true
        };

        context.ConsultationServices.Add(service);
        await context.SaveChangesAsync();

        var consultantId = await SeedConsultantAsync(context, service.Id, "Rakib");

        return (service.Id, consultantId);
    }

    private static async Task<long> SeedConsultantAsync(
        DataContext context, long serviceId, string name)
    {
        var consultant = new Consultant
        {
            Name = name,
            IsActive = true,
            Services = [new ConsultantService { ConsultationServiceId = serviceId }]
        };

        context.Consultants.Add(consultant);
        await context.SaveChangesAsync();

        return consultant.Id;
    }

    private static Booking Booking(
        long serviceId, long consultantId, string number, DateTimeOffset startUtc) =>
        new()
        {
            BookingNumber = number,
            ConsultationServiceId = serviceId,
            ConsultantId = consultantId,
            ContactName = "Rakib Hasan",
            ContactPhone = "+8801712349999",
            ScheduledAtUtc = startUtc,
            DurationMinutes = 60,
            Currency = Money.Bdt,
            Fee = Money.Taka(2_000m),
            Status = BookingStatus.Requested
        };
}
