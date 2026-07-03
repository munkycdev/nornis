using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Description)
            .HasMaxLength(2000);

        builder.Property(c => c.GameSystem)
            .HasMaxLength(200);

        builder.Property(c => c.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.RowVersion)
            .IsRowVersion();

        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
