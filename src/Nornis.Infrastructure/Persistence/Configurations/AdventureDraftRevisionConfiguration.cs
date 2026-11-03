using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class AdventureDraftRevisionConfiguration : IEntityTypeConfiguration<AdventureDraftRevision>
{
    public void Configure(EntityTypeBuilder<AdventureDraftRevision> builder)
    {
        builder.ToTable("AdventureDraftRevisions");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.BodyMarkdown)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(r => r.Reason)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(r => r.CreatedAt)
            .HasColumnType("datetimeoffset");

        // Undo reads the newest; the trim deletes the oldest past the bound.
        builder.HasIndex(r => new { r.AdventureId, r.CreatedAt });

        // Adventures is the only path here (Worlds → Adventures → revisions), so it can cascade.
        builder.HasOne<Adventure>()
            .WithMany()
            .HasForeignKey(r => r.AdventureId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
