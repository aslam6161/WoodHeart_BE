using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Common;

namespace WoodHeart.Repository.Configurations.Common;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        // The delivery worker's only query: "what is due to be sent right now?"
        // Filtered, so the index stays small even after a million processed rows.
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt })
            .HasDatabaseName("ix_outbox_messages_due")
            .HasFilter("status IN ('Pending', 'Processing')");

        // Guards at-least-once delivery against creating duplicate side effects —
        // which for SMS means a duplicate line on the gateway invoice.
        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_outbox_messages_idempotency_key")
            .HasFilter("idempotency_key IS NOT NULL");

        builder.HasIndex(x => x.CreatedAt);
    }
}

public class StoreSettingConfiguration : IEntityTypeConfiguration<StoreSetting>
{
    public void Configure(EntityTypeBuilder<StoreSetting> builder)
    {
        builder.ToTable("store_settings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512);
        builder.Property(x => x.ValueType).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(x => x.Key).IsUnique();
        builder.HasIndex(x => x.Category);
    }
}

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flags");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512);

        builder.HasIndex(x => x.Name).IsUnique();
    }
}
