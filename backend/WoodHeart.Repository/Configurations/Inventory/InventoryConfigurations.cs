using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Inventory;

namespace WoodHeart.Repository.Configurations.Inventory;

public class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OnHand).IsRequired();
        builder.Property(x => x.Reserved).IsRequired();

        builder.Ignore(x => x.Available);

        builder.HasOne(x => x.ProductVariant)
            .WithOne(x => x.Stock)
            .HasForeignKey<StockItem>(x => x.ProductVariantId)
            // The count outlives nothing: a variant that is gone has no shelf.
            // But variants are soft-deleted, so this never actually fires.
            .OnDelete(DeleteBehavior.Cascade);

        // One count per variant. The place this widens when a second
        // warehouse arrives.
        builder.HasIndex(x => x.ProductVariantId)
            .IsUnique()
            .HasDatabaseName("ux_stock_items_variant");

        // The database's own word on it, in case some future code path
        // writes the column directly.
        builder.ToTable(t => t.HasCheckConstraint("ck_stock_items_on_hand", "on_hand >= 0"));
        builder.ToTable(t => t.HasCheckConstraint("ck_stock_items_reserved", "reserved >= 0"));
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.OnHandAfter).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(120);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.PerformedBy).HasMaxLength(120).IsRequired();

        builder.HasOne(x => x.StockItem)
            .WithMany()
            .HasForeignKey(x => x.StockItemId)
            // The ledger is the record. Nothing that deletes a count may take
            // its history with it.
            .OnDelete(DeleteBehavior.Restrict);

        // The ledger for one variant, newest first — the admin's "what
        // happened to this" view.
        builder.HasIndex(x => new { x.ProductVariantId, x.OccurredAt })
            .HasDatabaseName("ix_stock_movements_variant_occurred");

        // "What did this order do to stock", for a return or a dispute.
        builder.HasIndex(x => x.OrderId)
            .HasDatabaseName("ix_stock_movements_order")
            .HasFilter("order_id IS NOT NULL");
    }
}

public class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.StockItem)
            .WithMany()
            .HasForeignKey(x => x.StockItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // The order lifecycle finds its holds by order; the Available
        // arithmetic never queries this table, it reads StockItem.Reserved.
        builder.HasIndex(x => x.OrderId).HasDatabaseName("ix_stock_reservations_order");

        // Active holds per variant, for the day a reservation expiry job or a
        // "who is holding my stock" screen needs them.
        builder.HasIndex(x => new { x.ProductVariantId, x.Status })
            .HasDatabaseName("ix_stock_reservations_variant_status");
    }
}
