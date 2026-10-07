using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WoodHeart.Domain.Entity.Support;

namespace WoodHeart.Repository.Configurations.Support;

public class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> builder)
    {
        builder.ToTable("contact_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();

        // E.164, so the same customer writing in twice from two spellings of
        // their number is one searchable string.
        builder.Property(x => x.Phone).HasMaxLength(20);

        builder.Property(x => x.Email).HasMaxLength(256);

        builder.Property(x => x.Reference).HasMaxLength(40);

        builder.Property(x => x.Message).HasMaxLength(2000).IsRequired();

        builder.Property(x => x.StaffNote).HasMaxLength(1000);

        // The inbox opens on "what has nobody read yet", newest first, and that
        // is the only query it runs often enough to index for.
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_contact_messages_status_created_at");
    }
}
