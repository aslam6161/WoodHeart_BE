using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Repository.Configurations.Consultations;

/// <summary>Shared by the two string lists in this module.</summary>
internal static class StringListStorage
{
    public static readonly ValueComparer<List<string>> Comparer =
        new(
            (left, right) => left!.SequenceEqual(right!, StringComparer.Ordinal),
            values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
            values => values.ToList());
}

public class ConsultationServiceConfiguration : IEntityTypeConfiguration<ConsultationService>
{
    public void Configure(EntityTypeBuilder<ConsultationService> builder)
    {
        builder.ToTable("consultation_services");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasConversion(ValueObjectConverters.LocalizedText, ValueObjectConverters.LocalizedTextComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Slug)
            .HasConversion(ValueObjectConverters.Slug, ValueObjectConverters.SlugComparer)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasConversion(ValueObjectConverters.LocalizedText!, ValueObjectConverters.LocalizedTextComparer!)
            .HasColumnType("jsonb");

        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.DurationMinutes).IsRequired();

        builder.Property(x => x.Fee)
            .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.AdvanceAmount)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        // One service per URL. Filtered, because a soft-deleted service keeps
        // its slug and a new one must still be able to take it.
        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_consultation_services_slug");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_consultation_services_duration", "duration_minutes > 0"));
    }
}

public class ConsultantConfiguration : IEntityTypeConfiguration<Consultant>
{
    public void Configure(EntityTypeBuilder<Consultant> builder)
    {
        builder.ToTable("consultants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.PhotoPath).HasMaxLength(512);

        builder.Property(x => x.Bio)
            .HasConversion(ValueObjectConverters.LocalizedText!, ValueObjectConverters.LocalizedTextComparer!)
            .HasColumnType("jsonb");

        builder.Property(x => x.Specialities)
            .HasConversion(
                values => values.ToArray(),
                stored => stored.ToList(),
                StringListStorage.Comparer)
            .HasColumnType("text[]")
            .IsRequired();
    }
}

public class ConsultantServiceConfiguration : IEntityTypeConfiguration<ConsultantService>
{
    public void Configure(EntityTypeBuilder<ConsultantService> builder)
    {
        builder.ToTable("consultant_services");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Consultant)
            .WithMany(x => x.Services)
            .HasForeignKey(x => x.ConsultantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ConsultationService)
            .WithMany(x => x.Consultants)
            .HasForeignKey(x => x.ConsultationServiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ConsultantId, x.ConsultationServiceId })
            .IsUnique()
            .HasDatabaseName("ux_consultant_services_pair");
    }
}

public class AvailabilityRuleConfiguration : IEntityTypeConfiguration<AvailabilityRule>
{
    public void Configure(EntityTypeBuilder<AvailabilityRule> builder)
    {
        builder.ToTable("availability_rules");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DayOfWeek).HasConversion<string>().HasMaxLength(12).IsRequired();
        builder.Property(x => x.StartTime).IsRequired();
        builder.Property(x => x.EndTime).IsRequired();
        builder.Property(x => x.SlotMinutes).IsRequired();

        builder.HasOne(x => x.Consultant)
            .WithMany(x => x.AvailabilityRules)
            .HasForeignKey(x => x.ConsultantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ConsultantId, x.DayOfWeek })
            .HasDatabaseName("ix_availability_rules_consultant_day");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_availability_rules_window", "end_time > start_time"));

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_availability_rules_slot", "slot_minutes > 0"));
    }
}

public class AvailabilityExceptionConfiguration : IEntityTypeConfiguration<AvailabilityException>
{
    public void Configure(EntityTypeBuilder<AvailabilityException> builder)
    {
        builder.ToTable("availability_exceptions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(300);

        builder.HasOne(x => x.Consultant)
            .WithMany(x => x.AvailabilityExceptions)
            .HasForeignKey(x => x.ConsultantId)
            .OnDelete(DeleteBehavior.Cascade);

        // One statement per consultant per day. Two rows saying different
        // things about the same Eid is a schedule nobody can reason about.
        builder.HasIndex(x => new { x.ConsultantId, x.Date })
            .IsUnique()
            .HasDatabaseName("ux_availability_exceptions_consultant_date");
    }
}

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.BookingNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ContactName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ContactPhone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.CustomerLanguage).HasMaxLength(5).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProjectBrief).HasMaxLength(2000);
        builder.Property(x => x.BudgetRange).HasMaxLength(60);
        builder.Property(x => x.InternalNotes).HasMaxLength(2000);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(120);

        builder.Property(x => x.RoomTypes)
            .HasConversion(
                values => values.ToArray(),
                stored => stored.ToList(),
                StringListStorage.Comparer)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(x => x.Fee)
            .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.AdvanceDue)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        builder.Ignore(x => x.EndsAtUtc);

        builder.OwnsOne(x => x.SiteAddress, address =>
        {
            address.Property(a => a.Division)
                .HasColumnName("site_division")
                .HasMaxLength(DeliveryAddress.MaxDivision);

            address.Property(a => a.District)
                .HasColumnName("site_district")
                .HasMaxLength(DeliveryAddress.MaxDistrict);

            address.Property(a => a.Upazila)
                .HasColumnName("site_upazila")
                .HasMaxLength(DeliveryAddress.MaxUpazila);

            address.Property(a => a.Area)
                .HasColumnName("site_area")
                .HasMaxLength(DeliveryAddress.MaxArea);

            address.Property(a => a.AddressLine)
                .HasColumnName("site_address_line")
                .HasMaxLength(DeliveryAddress.MaxAddressLine);

            address.Property(a => a.Landmark)
                .HasColumnName("site_landmark")
                .HasMaxLength(DeliveryAddress.MaxLandmark);

            address.Property(a => a.Postcode)
                .HasColumnName("site_postcode")
                .HasMaxLength(DeliveryAddress.MaxPostcode);
        });

        builder.HasOne(x => x.ConsultationService)
            .WithMany()
            .HasForeignKey(x => x.ConsultationServiceId)
            // The service must survive as long as any booking references it:
            // the booking snapshots what it needs to display, but "how many
            // site visits did we sell" needs the id to still mean something.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Consultant)
            .WithMany()
            .HasForeignKey(x => x.ConsultantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.BookingNumber)
            .IsUnique()
            .HasDatabaseName("ux_bookings_number");

        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_bookings_idempotency_key");

        // "What is in the diary this week", and the query behind availability.
        builder.HasIndex(x => new { x.ScheduledAtUtc, x.Status })
            .HasDatabaseName("ix_bookings_scheduled_status");

        builder.HasIndex(x => x.ContactPhone).HasDatabaseName("ix_bookings_contact_phone");

        // --- The one that actually prevents a double booking -----------------
        //
        // An application-level availability check loses this race under load:
        // two requests both read "four o'clock is free" and both write. The
        // index is the only thing that cannot, and losing it means two people
        // in the studio at once.
        //
        // Filtered to the statuses that still hold the slot — see
        // BookingStatusMachine.HoldsTheSlot, which the tests keep in step with
        // this list — so a cancelled booking frees its afternoon. A booking
        // with no consultant is excluded because it is not yet anybody's
        // afternoon to lose.
        builder.HasIndex(x => new { x.ConsultantId, x.ScheduledAtUtc })
            .IsUnique()
            .HasFilter(
                "consultant_id IS NOT NULL "
                + "AND status IN ('Requested', 'Confirmed', 'Rescheduled')")
            .HasDatabaseName("ux_bookings_consultant_slot");
    }
}

public class BookingTimelineEntryConfiguration : IEntityTypeConfiguration<BookingTimelineEntry>
{
    public void Configure(EntityTypeBuilder<BookingTimelineEntry> builder)
    {
        builder.ToTable("booking_timeline_entries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ActorName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(1000);

        builder.HasOne(x => x.Booking)
            .WithMany(x => x.Timeline)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.BookingId, x.OccurredAt })
            .HasDatabaseName("ix_booking_timeline_booking_occurred");
    }
}
