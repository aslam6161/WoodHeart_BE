using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Ordering;

namespace WoodHeart.Repository.Configurations.Ordering;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.DeliveryZone).HasConversion<string>().HasMaxLength(20);

        // Base64 of a SHA-256 digest. Fixed width, so the column is too.
        builder.Property(x => x.AnonymousToken).HasMaxLength(64);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            // A customer is never hard-deleted, but if one ever were, taking
            // their basket with them is right and taking their *orders* with
            // them would not be. Restrict is the default; this is deliberate.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.Cart)
            .HasForeignKey(x => x.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        // <b>One active cart per identity, enforced by the database.</b>
        //
        // Two tabs adding to an empty basket at the same moment is an ordinary
        // thing for a customer to do, and an application-level "find or create"
        // loses that race — the loser silently gets a second cart, and half the
        // items vanish when they check out. A filtered unique index turns that
        // into a constraint violation the service can retry.
        //
        // Filtered to Active so a customer's history of checked-out carts does
        // not collide with the one they are filling now.
        builder.HasIndex(x => x.CustomerId)
            .IsUnique()
            .HasDatabaseName("ux_carts_one_active_per_customer")
            .HasFilter("customer_id IS NOT NULL AND status = 'Active'");

        builder.HasIndex(x => x.AnonymousToken)
            .IsUnique()
            .HasDatabaseName("ux_carts_one_active_per_guest")
            .HasFilter("anonymous_token IS NOT NULL AND status = 'Active'");

        // The abandoned-cart sweep's only query.
        builder.HasIndex(x => new { x.Status, x.ExpiresAt })
            .HasDatabaseName("ix_carts_expiry")
            .HasFilter("status = 'Active'");
    }
}

public class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        builder.ToTable("cart_lines");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity).IsRequired();

        builder.Property(x => x.UnitPriceAtAdd)
            .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            // A variant that is in someone's basket must not be deletable out
            // from under them. Variants are soft-deleted anyway, so this is the
            // belt to that braces.
            .OnDelete(DeleteBehavior.Restrict);

        // Adding the same variant twice increments the existing line rather
        // than creating a second one — a basket showing "Segun bed ×1" twice is
        // a bug the customer notices immediately. Enforced here so that a race
        // between two tabs cannot produce it either.
        builder.HasIndex(x => new { x.CartId, x.ProductVariantId })
            .IsUnique()
            .HasDatabaseName("ux_cart_lines_one_per_variant");
    }
}
