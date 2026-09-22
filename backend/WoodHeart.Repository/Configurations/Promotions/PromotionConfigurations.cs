using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Ordering;

namespace WoodHeart.Repository.Configurations.Promotions;

public class DiscountConfiguration : IEntityTypeConfiguration<Discount>
{
    /// <summary>
    /// Stores the zone list as <c>text[]</c> rather than as ids.
    /// </summary>
    /// <remarks>
    /// Readable in psql, which is where somebody looks when a coupon is not
    /// applying — <c>{InsideDhaka}</c> answers the question and <c>{0}</c>
    /// starts another one. The comparer is what makes EF notice the list being
    /// replaced; without it a changed set of zones would save silently as the
    /// old one.
    /// </remarks>
    private static readonly ValueComparer<List<DeliveryZone>> ZoneComparer =
        new(
            (left, right) => left!.SequenceEqual(right!),
            zones => zones.Aggregate(0, (hash, zone) => HashCode.Combine(hash, zone)),
            zones => zones.ToList());

    private static readonly ValueComparer<List<string>> StringListComparer =
        new(
            (left, right) => left!.SequenceEqual(right!, StringComparer.Ordinal),
            values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
            values => values.ToList());

    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.ToTable("discounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(40);

        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(x => x.Value).HasColumnType("numeric(18,2)").IsRequired();

        builder.Property(x => x.MaxDiscountAmount)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        builder.Property(x => x.MinSubtotal)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        builder.Property(x => x.DeliveryZones)
            .HasConversion(
                zones => zones.Select(zone => zone.ToString()).ToArray(),
                values => values.Select(Enum.Parse<DeliveryZone>).ToList(),
                ZoneComparer)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(x => x.PaymentMethods)
            .HasConversion(
                methods => methods.ToArray(),
                values => values.ToList(),
                StringListComparer)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Ignore(x => x.IsCoupon);

        // One code, one discount. Partial, because every automatic promotion
        // has a null code and a plain unique index would allow only one of
        // them. Codes are stored upper-cased — see Discount.NormaliseCode —
        // so a plain index is enough and a case-insensitive lookup that could
        // not use it is avoided.
        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("code IS NOT NULL")
            .HasDatabaseName("ux_discounts_code");

        // The engine's own query: everything live right now. Status first
        // because it is the most selective — most rows in a shop that has been
        // running a year are Archived.
        builder.HasIndex(x => new { x.Status, x.StartsAt, x.EndsAt })
            .HasDatabaseName("ix_discounts_status_window");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_discounts_value", "value >= 0"));
    }
}

public class DiscountTargetConfiguration : IEntityTypeConfiguration<DiscountTarget>
{
    public void Configure(EntityTypeBuilder<DiscountTarget> builder)
    {
        builder.ToTable("discount_targets");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Discount)
            .WithMany(x => x.Targets)
            .HasForeignKey(x => x.DiscountId)
            // A target has no meaning without its discount, and unlike the
            // discount itself nothing historic points at it.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DiscountId).HasDatabaseName("ix_discount_targets_discount");

        // Exactly one of the two, said by the database as well as by the code.
        // A row with neither would match nothing and a row with both would
        // match twice, and both are silent.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_discount_targets_one_of",
            "(category_id IS NULL) <> (product_id IS NULL)"));
    }
}

public class PromotionUsageConfiguration : IEntityTypeConfiguration<PromotionUsage>
{
    public void Configure(EntityTypeBuilder<PromotionUsage> builder)
    {
        builder.ToTable("promotion_usages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ContactPhone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(40);

        builder.Property(x => x.Amount)
            .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.HasOne(x => x.Discount)
            .WithMany()
            .HasForeignKey(x => x.DiscountId)
            // The record of what a discount cost outlives any wish to delete
            // the discount. Archiving is the way out.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        // "How many times has this been used" — the query behind every usage
        // limit, run on the busiest page of the site.
        builder.HasIndex(x => x.DiscountId).HasDatabaseName("ix_promotion_usages_discount");

        // "...and how many times by this person", for a guest identified by
        // their phone number.
        builder.HasIndex(x => new { x.DiscountId, x.ContactPhone })
            .HasDatabaseName("ix_promotion_usages_discount_phone");

        builder.HasIndex(x => x.OrderId).HasDatabaseName("ix_promotion_usages_order");

        // One row per discount per order. A retried placement or a second
        // save must not count a redemption twice, and the limit is only as
        // honest as this index.
        builder.HasIndex(x => new { x.DiscountId, x.OrderId })
            .IsUnique()
            .HasDatabaseName("ux_promotion_usages_discount_order");
    }
}
