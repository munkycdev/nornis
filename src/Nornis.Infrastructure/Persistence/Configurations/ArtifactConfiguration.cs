using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ArtifactConfiguration : IEntityTypeConfiguration<Artifact>
{
    public void Configure(EntityTypeBuilder<Artifact> builder)
    {
        builder.ToTable("Artifacts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(200);

        // Assigned once by SlugAssigner from the name; unique per world, null only until backfilled.
        builder.Property(a => a.Slug)
            .HasMaxLength(Slug.MaxStoredLength);

        builder.HasIndex(a => new { a.WorldId, a.Slug })
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL");

        // Kept explicitly: the filtered composite above starts with WorldId, which makes the
        // convention drop the plain FK index as redundant — but a filtered index cannot serve
        // "every artifact in this world", the most common query on the table.
        builder.HasIndex(a => a.WorldId);

        builder.Property(a => a.Summary)
            .HasMaxLength(2000);

        builder.Property(a => a.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Visibility)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Confidence)
            .HasPrecision(5, 4);

        builder.Property(a => a.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(a => a.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(a => a.RowVersion)
            .IsRowVersion();

        builder.HasOne(a => a.World)
            .WithMany()
            .HasForeignKey(a => a.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not cascade: the artifact already cascades from World, and users
        // must not take knowledge rows with them (mirrors Source.CreatedByUser).
        builder.HasOne(a => a.CreatedByUser)
            .WithMany()
            .HasForeignKey(a => a.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
