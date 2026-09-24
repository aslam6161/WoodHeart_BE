using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Notifications;

namespace WoodHeart.Repository.Configurations.Notifications;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");

        builder.HasKey(x => x.Id);

        // Matches OutboxMessage.Type, which is what joins a row here to the
        // messages it governs.
        builder.Property(x => x.Code).HasMaxLength(128).IsRequired();

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("ux_notification_templates_code");
    }
}
