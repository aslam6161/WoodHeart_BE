using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Repository.Configurations.Ordering;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderNumber).HasMaxLength(24).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();

        builder.Property(x => x.ContactName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ContactPhone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ContactEmail).HasMaxLength(256);

        builder.Property(x => x.DeliveryNote).HasMaxLength(500);
        builder.Property(x => x.InternalNotes).HasMaxLength(2000);
        builder.Property(x => x.PaymentMethodCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.FulfilmentStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.DeliveryZone).HasConversion<string>().HasMaxLength(20);

        builder.Property(x => x.VatRatePercent).HasColumnType("numeric(5,2)");

        foreach (var money in new[]
                 {
                     nameof(Order.Subtotal),
                     nameof(Order.DiscountTotal),
                     nameof(Order.GoodsNet),
                     nameof(Order.VatAmount),
                     nameof(Order.DeliveryFee),
                     nameof(Order.PaymentSurcharge),
                     nameof(Order.GrandTotal)
                 })
        {
            builder.Property<Money>(money)
                .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
                .HasColumnType("numeric(18,2)")
                .IsRequired();
        }

        builder.Property(x => x.RequiredAdvanceAmount)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        // Owned rather than a related table: an address only ever exists as
        // part of the order it was typed for, and it is read on every single
        // read of that order. A join for something that is never queried
        // independently is a join for nothing.
        builder.OwnsOne(x => x.ShippingAddress, address =>
        {
            address.Property(a => a.Division)
                .HasColumnName("shipping_division")
                .HasMaxLength(DeliveryAddress.MaxDivision)
                .IsRequired();

            address.Property(a => a.District)
                .HasColumnName("shipping_district")
                .HasMaxLength(DeliveryAddress.MaxDistrict)
                .IsRequired();

            address.Property(a => a.Upazila)
                .HasColumnName("shipping_upazila")
                .HasMaxLength(DeliveryAddress.MaxUpazila);

            address.Property(a => a.Area)
                .HasColumnName("shipping_area")
                .HasMaxLength(DeliveryAddress.MaxArea);

            address.Property(a => a.AddressLine)
                .HasColumnName("shipping_address_line")
                .HasMaxLength(DeliveryAddress.MaxAddressLine)
                .IsRequired();

            address.Property(a => a.Landmark)
                .HasColumnName("shipping_landmark")
                .HasMaxLength(DeliveryAddress.MaxLandmark);

            address.Property(a => a.Postcode)
                .HasColumnName("shipping_postcode")
                .HasMaxLength(DeliveryAddress.MaxPostcode);
        });

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            // Restrict, unlike the cart's Cascade. A basket may follow its owner
            // out; an order may not. It is a financial record, and a guest order
            // has no owner to follow in the first place.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.Order)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Timeline)
            .WithOne(x => x.Order)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.OrderNumber)
            .IsUnique()
            .HasDatabaseName("ux_orders_order_number");

        // <b>The index that stops a double-tap becoming two sofas.</b>
        //
        // A customer on a slow connection presses "Place order" twice, or the
        // browser retries a POST that timed out after the order was already
        // written. Both send the same key, and the second insert is rejected
        // here rather than producing a second order — which is a refund, a
        // phone call, and a van that goes out twice.
        //
        // An application-level check loses this race; the two requests are
        // often on different connections a millisecond apart.
        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_orders_idempotency_key")
            .HasFilter("idempotency_key IS NOT NULL");

        // "My orders", newest first.
        builder.HasIndex(x => new { x.CustomerId, x.PlacedAt })
            .HasDatabaseName("ix_orders_customer_placed")
            .HasFilter("customer_id IS NOT NULL");

        // How a guest order is found again — and how it is claimed when that
        // guest registers with the same number later.
        builder.HasIndex(x => x.ContactPhone).HasDatabaseName("ix_orders_contact_phone");

        // The admin order list: a status filter over a date range.
        builder.HasIndex(x => new { x.Status, x.PlacedAt }).HasDatabaseName("ix_orders_status_placed");
    }
}

public class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProductNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProductNameBn).HasMaxLength(200);
        builder.Property(x => x.ProductSlug).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.VariantName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ImagePath).HasMaxLength(500);
        builder.Property(x => x.Quantity).IsRequired();

        foreach (var money in new[]
                 {
                     nameof(OrderLine.UnitPrice),
                     nameof(OrderLine.DiscountAmount),
                     nameof(OrderLine.LineTotal),
                     nameof(OrderLine.DeliveryChargeApplied)
                 })
        {
            builder.Property<Money>(money)
                .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
                .HasColumnType("numeric(18,2)")
                .IsRequired();
        }

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            // The variant must survive as long as any order references it —
            // not because the line needs it to render (every display field is
            // copied) but because "how many did we sell" needs the id to mean
            // something.
            .OnDelete(DeleteBehavior.Restrict);

        // Sales reporting by product, and "you ordered this before" on a
        // product page.
        builder.HasIndex(x => x.ProductVariantId).HasDatabaseName("ix_order_lines_variant");
        builder.HasIndex(x => x.ProductId).HasDatabaseName("ix_order_lines_product");
    }
}

public class OrderTimelineEntryConfiguration : IEntityTypeConfiguration<OrderTimelineEntry>
{
    public void Configure(EntityTypeBuilder<OrderTimelineEntry> builder)
    {
        builder.ToTable("order_timeline_entries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ActorName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(1000);

        // The order detail page reads the whole history in order, every time.
        builder.HasIndex(x => new { x.OrderId, x.OccurredAt })
            .HasDatabaseName("ix_order_timeline_order_occurred");
    }
}
