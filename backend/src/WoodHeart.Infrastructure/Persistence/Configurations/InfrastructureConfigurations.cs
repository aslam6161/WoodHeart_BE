using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Infrastructure.Identity;
using WoodHeart.Infrastructure.Persistence.Outbox;
using WoodHeart.Infrastructure.Persistence.Settings;

namespace WoodHeart.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
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
        // Filtered so the index stays small even after a million processed rows.
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc })
            .HasDatabaseName("ix_outbox_messages_due")
            .HasFilter("status IN ('Pending', 'Processing')");

        // Guards at-least-once delivery against creating duplicate side effects.
        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_outbox_messages_idempotency_key")
            .HasFilter("idempotency_key IS NOT NULL");

        builder.HasIndex(x => x.CreatedAtUtc);
    }
}

internal sealed class StoreSettingConfiguration : IEntityTypeConfiguration<StoreSetting>
{
    public void Configure(EntityTypeBuilder<StoreSetting> builder)
    {
        builder.ToTable("store_settings");

        builder.HasKey(x => x.Key);

        builder.Property(x => x.Key).HasMaxLength(128);
        builder.Property(x => x.Value).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512);
        builder.Property(x => x.ModifiedBy).HasMaxLength(256);
        builder.Property(x => x.ValueType).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(x => x.Category);
    }
}

internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flags");

        builder.HasKey(x => x.Name);

        builder.Property(x => x.Name).HasMaxLength(128);
        builder.Property(x => x.Description).HasMaxLength(512);
        builder.Property(x => x.ModifiedBy).HasMaxLength(256);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedByIp).HasMaxLength(64);
        builder.Property(x => x.RevokedByIp).HasMaxLength(64);

        builder.Ignore(x => x.IsActive);

        // Every refresh presents a token hash — this is the lookup that must be fast.
        builder.HasIndex(x => x.TokenHash).IsUnique();

        // Used when revoking a whole family after a detected reuse.
        builder.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Renames the ASP.NET Identity tables from their default PascalCase
/// (<c>AspNetUsers</c>) to the snake_case convention the rest of the schema uses.
/// </summary>
/// <remarks>
/// Purely for consistency: a DBA opening this database should not find two
/// naming styles depending on which framework created the table.
/// </remarks>
internal sealed class IdentityTableNaming :
    IEntityTypeConfiguration<ApplicationUser>,
    IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("users");

        builder.Property(x => x.FullName).HasMaxLength(256);
        builder.Property(x => x.PreferredLanguage).HasMaxLength(8);

        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.IsActive);
    }

    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.ToTable("roles");

        builder.Property(x => x.Description).HasMaxLength(512);
    }
}
