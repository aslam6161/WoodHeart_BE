using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Quotations;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Repository.Configurations.Quotations;

public class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("quotations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.QuotationNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ContactName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ContactPhone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.CustomerLanguage).HasMaxLength(5).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.DeliveryZone).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(4000);
        builder.Property(x => x.InternalNotes).HasMaxLength(2000);
        builder.Property(x => x.DeclineReason).HasMaxLength(500);
        builder.Property(x => x.VatRatePercent).HasColumnType("numeric(5,2)");

        foreach (var money in new[]
                 {
                     nameof(Quotation.Subtotal),
                     nameof(Quotation.DiscountTotal),
                     nameof(Quotation.GoodsNet),
                     nameof(Quotation.VatAmount),
                     nameof(Quotation.DeliveryFee),
                     nameof(Quotation.GrandTotal)
                 })
        {
            builder.Property<Money>(money)
                .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
                .HasColumnType("numeric(18,2)")
                .IsRequired();
        }

        builder.Property(x => x.DeliveryFeeOverride)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        builder.Ignore(x => x.IsOpen);

        builder.OwnsOne(x => x.ShippingAddress, address =>
        {
            address.Property(a => a.Division)
                .HasColumnName("ship_division")
                .HasMaxLength(DeliveryAddress.MaxDivision);

            address.Property(a => a.District)
                .HasColumnName("ship_district")
                .HasMaxLength(DeliveryAddress.MaxDistrict);

            address.Property(a => a.Upazila)
                .HasColumnName("ship_upazila")
                .HasMaxLength(DeliveryAddress.MaxUpazila);

            address.Property(a => a.Area)
                .HasColumnName("ship_area")
                .HasMaxLength(DeliveryAddress.MaxArea);

            address.Property(a => a.AddressLine)
                .HasColumnName("ship_address_line")
                .HasMaxLength(DeliveryAddress.MaxAddressLine);

            address.Property(a => a.Landmark)
                .HasColumnName("ship_landmark")
                .HasMaxLength(DeliveryAddress.MaxLandmark);

            address.Property(a => a.Postcode)
                .HasColumnName("ship_postcode")
                .HasMaxLength(DeliveryAddress.MaxPostcode);
        });

        // Restrict throughout. A quotation is evidence of what was offered, and
        // deleting a booking or an order must never be allowed to take the
        // record of the offer with it.
        builder.HasOne(x => x.Booking)
            .WithMany()
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ConvertedOrder)
            .WithMany()
            .HasForeignKey(x => x.ConvertedOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.QuotationNumber)
            .IsUnique()
            .HasDatabaseName("ux_quotations_number");

        // "Which quotations are still out there", and the job that expires them.
        builder.HasIndex(x => new { x.Status, x.ValidUntil })
            .HasDatabaseName("ix_quotations_status_valid_until");

        builder.HasIndex(x => x.ContactPhone).HasDatabaseName("ix_quotations_contact_phone");

        builder.HasIndex(x => x.BookingId).HasDatabaseName("ix_quotations_booking");

        // One order per quotation, and one quotation per order. A second
        // conversion of the same quotation is two sofas the customer asked for
        // once, and the index is what makes the race unwinnable rather than
        // unlikely.
        builder.HasIndex(x => x.ConvertedOrderId)
            .IsUnique()
            .HasFilter("converted_order_id IS NOT NULL")
            .HasDatabaseName("ux_quotations_converted_order");
    }
}

public class QuotationLineConfiguration : IEntityTypeConfiguration<QuotationLine>
{
    public void Configure(EntityTypeBuilder<QuotationLine> builder)
    {
        builder.ToTable("quotation_lines");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.VariantName).HasMaxLength(200);
        builder.Property(x => x.Sku).HasMaxLength(64);
        builder.Property(x => x.ImagePath).HasMaxLength(512);

        builder.Property(x => x.UnitPrice)
            .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.LineTotal)
            .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Ignore(x => x.HoldsStock);

        builder.HasOne(x => x.Quotation)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.QuotationId)
            // The lines are the quotation. Deleting a draft takes them with it;
            // nothing else deletes a quotation.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.QuotationId).HasDatabaseName("ix_quotation_lines_quotation");

        // A line with no variant is made to measure, and the check keeps the
        // pairing honest: the catalogue fields only mean something together.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_quotation_lines_catalogue",
            "(product_variant_id IS NULL AND product_id IS NULL) "
            + "OR (product_variant_id IS NOT NULL AND product_id IS NOT NULL)"));

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_quotation_lines_quantity", "quantity > 0"));
    }
}
