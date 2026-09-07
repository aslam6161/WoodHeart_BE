using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Payments;

namespace WoodHeart.Repository.Configurations.Payments;

public class PaymentMethodConfigConfiguration : IEntityTypeConfiguration<PaymentMethodConfig>
{
    public void Configure(EntityTypeBuilder<PaymentMethodConfig> builder)
    {
        builder.ToTable("payment_method_configs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();

        builder.Property(x => x.DisplayName)
            .HasConversion(ValueObjectConverters.LocalizedText, ValueObjectConverters.LocalizedTextComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Description)
            .HasConversion(ValueObjectConverters.LocalizedText!, ValueObjectConverters.LocalizedTextComparer!)
            .HasColumnType("jsonb");

        builder.Property(x => x.IconUrl).HasMaxLength(500);
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ChargeType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ChargeValue).HasColumnType("numeric(18,2)");

        builder.Property(x => x.MinOrderAmount)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        builder.Property(x => x.MaxOrderAmount)
            .HasConversion(ValueObjectConverters.Money!, ValueObjectConverters.MoneyComparer!)
            .HasColumnType("numeric(18,2)");

        // Encrypted before it gets here, and long: a gateway's credential blob
        // grows by roughly a third under Data Protection's envelope.
        builder.Property(x => x.Credentials).HasMaxLength(4000);

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("ux_payment_method_configs_code");
    }
}
