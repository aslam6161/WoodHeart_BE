using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Repository.Configurations.Catalog;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasConversion(ValueObjectConverters.LocalizedText, ValueObjectConverters.LocalizedTextComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Description)
            .HasConversion(ValueObjectConverters.LocalizedText!, ValueObjectConverters.LocalizedTextComparer!)
            .HasColumnType("jsonb");

        builder.Property(x => x.Slug)
            .HasConversion(ValueObjectConverters.Slug, ValueObjectConverters.SlugComparer)
            .HasMaxLength(Slug.MaxLength)
            .IsRequired();

        builder.Property(x => x.MaterializedPath).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ImagePath).HasMaxLength(512);
        builder.Property(x => x.SeoTitle).HasMaxLength(200);
        builder.Property(x => x.SeoDescription).HasMaxLength(400);

        // Restrict, not Cascade. Deleting a category that still has children
        // should be refused with a message, not silently take the subtree with
        // it — and on a soft-deleted entity a cascade would orphan rows that
        // the query filter then hides, which is the worst of both.
        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique among live rows only. A soft-deleted category must not hold
        // its slug hostage for ever.
        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasDatabaseName("ux_categories_slug")
            .HasFilter("is_deleted = false");

        // The tree read: "everything under /1/14/" as a prefix scan.
        builder.HasIndex(x => x.MaterializedPath).HasDatabaseName("ix_categories_path");

        builder.HasIndex(x => new { x.ParentId, x.SortOrder }).HasDatabaseName("ix_categories_parent_sort");
    }
}

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasConversion(ValueObjectConverters.LocalizedText, ValueObjectConverters.LocalizedTextComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Description)
            .HasConversion(ValueObjectConverters.LocalizedText!, ValueObjectConverters.LocalizedTextComparer!)
            .HasColumnType("jsonb");

        builder.Property(x => x.Slug)
            .HasConversion(ValueObjectConverters.Slug, ValueObjectConverters.SlugComparer)
            .HasMaxLength(Slug.MaxLength)
            .IsRequired();

        builder.Property(x => x.LogoPath).HasMaxLength(512);

        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasDatabaseName("ux_brands_slug")
            .HasFilter("is_deleted = false");
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();

        builder.Property(x => x.Name)
            .HasConversion(ValueObjectConverters.LocalizedText, ValueObjectConverters.LocalizedTextComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.ShortDescription)
            .HasConversion(ValueObjectConverters.LocalizedText!, ValueObjectConverters.LocalizedTextComparer!)
            .HasColumnType("jsonb");

        builder.Property(x => x.Description)
            .HasConversion(ValueObjectConverters.LocalizedText!, ValueObjectConverters.LocalizedTextComparer!)
            .HasColumnType("jsonb");

        builder.Property(x => x.Slug)
            .HasConversion(ValueObjectConverters.Slug, ValueObjectConverters.SlugComparer)
            .HasMaxLength(Slug.MaxLength)
            .IsRequired();

        builder.Property(x => x.BasePrice)
            .HasConversion(ValueObjectConverters.Money, ValueObjectConverters.MoneyComparer)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.CompareAtPrice)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        builder.Property(x => x.DeliverySurcharge)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        builder.Property(x => x.ProductType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(x => x.SearchText).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Material).HasMaxLength(128);
        builder.Property(x => x.FinishType).HasMaxLength(128);
        builder.Property(x => x.SeoTitle).HasMaxLength(200);
        builder.Property(x => x.SeoDescription).HasMaxLength(400);
        builder.Property(x => x.OgImagePath).HasMaxLength(512);

        // 3,2 not 18,2: a rating is 0.00 to 5.00 and the default decimal
        // convention would otherwise widen it pointlessly.
        builder.Property(x => x.AverageRating).HasColumnType("numeric(3,2)");

        builder.HasOne(x => x.Category)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Brand)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasDatabaseName("ux_products_slug")
            .HasFilter("is_deleted = false");

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("ux_products_code")
            .HasFilter("is_deleted = false");

        // The storefront's default listing: a category page, newest first.
        builder.HasIndex(x => new { x.CategoryId, x.Status, x.PublishedAt })
            .HasDatabaseName("ix_products_category_status_published");

        // "New arrivals" and the sitemap, both of which scan only live rows.
        builder.HasIndex(x => new { x.Status, x.PublishedAt })
            .HasDatabaseName("ix_products_status_published")
            .HasFilter("is_deleted = false");

        builder.HasIndex(x => x.IsFeatured)
            .HasDatabaseName("ix_products_featured")
            .HasFilter("is_featured = true AND is_deleted = false");
    }
}

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.VariantName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Barcode).HasMaxLength(64);

        builder.Property(x => x.OptionValues)
            .HasConversion(ValueObjectConverters.OptionValues, ValueObjectConverters.OptionValuesComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.PriceOverride)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        builder.Property(x => x.CompareAtPriceOverride)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        // Cascade here, unlike everywhere else: a variant has no meaning apart
        // from its product. Both are soft-deleted in practice, so this only
        // fires on a genuine hard delete of a mistake.
        builder.HasOne(x => x.Product)
            .WithMany(x => x.Variants)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.Sku)
            .IsUnique()
            .HasDatabaseName("ux_product_variants_sku")
            .HasFilter("is_deleted = false");

        // Exactly one default per product, enforced by the database rather than
        // by hoping the service gets it right. Two defaults means the product
        // page picks one arbitrarily and the price shown depends on row order.
        builder.HasIndex(x => x.ProductId)
            .IsUnique()
            .HasDatabaseName("ux_product_variants_one_default")
            .HasFilter("is_default = true AND is_deleted = false");

        builder.HasIndex(x => new { x.ProductId, x.SortOrder })
            .HasDatabaseName("ix_product_variants_product_sort");

        // GIN over the option map, which is what makes faceted filtering work:
        //   WHERE option_values @> '{"Wood":"Segun"}'
        // A btree index cannot answer that. Created explicitly because EF has
        // no first-class GIN support.
        builder.HasIndex(x => x.OptionValues)
            .HasMethod("gin")
            .HasDatabaseName("ix_product_variants_options_gin");
    }
}

public class ProductMediaConfiguration : IEntityTypeConfiguration<ProductMedia>
{
    public void Configure(EntityTypeBuilder<ProductMedia> builder)
    {
        builder.ToTable("product_media");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StoragePath).HasMaxLength(512).IsRequired();
        builder.Property(x => x.AltText).HasMaxLength(300);
        builder.Property(x => x.Caption).HasMaxLength(500);
        builder.Property(x => x.ContentType).HasMaxLength(100);
        builder.Property(x => x.ExternalUrl).HasMaxLength(1000);
        builder.Property(x => x.MediaType).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.Product)
            .WithMany(x => x.Media)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting one variant must not delete a photo the product still needs;
        // the media simply stops being variant-specific.
        builder.HasOne(x => x.Variant)
            .WithMany(x => x.Media)
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.ProductId, x.SortOrder })
            .HasDatabaseName("ix_product_media_product_sort");

        // One hero image per product. Without this, "the primary image" is
        // whichever row the planner returns first, and it can change between
        // deployments.
        builder.HasIndex(x => x.ProductId)
            .IsUnique()
            .HasDatabaseName("ux_product_media_one_primary")
            .HasFilter("is_primary = true AND is_deleted = false");
    }
}

public class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("collections");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasConversion(ValueObjectConverters.LocalizedText, ValueObjectConverters.LocalizedTextComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Description)
            .HasConversion(ValueObjectConverters.LocalizedText!, ValueObjectConverters.LocalizedTextComparer!)
            .HasColumnType("jsonb");

        builder.Property(x => x.Slug)
            .HasConversion(ValueObjectConverters.Slug, ValueObjectConverters.SlugComparer)
            .HasMaxLength(Slug.MaxLength)
            .IsRequired();

        builder.Property(x => x.BannerPath).HasMaxLength(512);
        builder.Property(x => x.ThumbnailPath).HasMaxLength(512);
        builder.Property(x => x.SeoTitle).HasMaxLength(200);
        builder.Property(x => x.SeoDescription).HasMaxLength(400);

        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasDatabaseName("ux_collections_slug")
            .HasFilter("is_deleted = false");

        builder.HasIndex(x => new { x.IsActive, x.StartsAt, x.EndsAt })
            .HasDatabaseName("ix_collections_schedule");

        // Explicit join table. EF would generate one automatically, but naming
        // it and its columns means the migration, the SQL you read in a slow
        // query log, and any future payload column are all predictable.
        builder.HasMany(x => x.Products)
            .WithMany(x => x.Collections)
            .UsingEntity(join =>
            {
                join.ToTable("collection_products");
                join.Property<long>("CollectionsId").HasColumnName("collection_id");
                join.Property<long>("ProductsId").HasColumnName("product_id");
            });
    }
}
