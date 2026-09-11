using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;

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

        // Assigned once by SlugAssigner from the name; unique per world, null only until backfilled.
        builder.Property(c => c.Slug)
            .HasMaxLength(Slug.MaxStoredLength);

        builder.HasIndex(c => new { c.WorldId, c.Slug })
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL");

        builder.Property(c => c.Description)
            .HasMaxLength(2000);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(c => c.StartedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.EndedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(c => c.WorldId);

        builder.HasOne(c => c.World)
            .WithMany(w => w.Campaigns)
            .HasForeignKey(c => c.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
