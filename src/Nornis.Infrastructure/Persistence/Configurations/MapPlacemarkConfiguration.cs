using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class MapPlacemarkConfiguration : IEntityTypeConfiguration<MapPlacemark>
{
    public void Configure(EntityTypeBuilder<MapPlacemark> builder)
    {
        builder.ToTable("MapPlacemarks");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.X)
            .HasPrecision(9, 8);

        builder.Property(p => p.Y)
            .HasPrecision(9, 8);

        builder.Property(p => p.Confidence)
            .HasPrecision(5, 4);

        builder.Property(p => p.Label)
            .HasMaxLength(200);

        builder.Property(p => p.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(p => p.UpdatedAt)
            .HasColumnType("datetimeoffset");

        // The only enforced FK: pins die with their map attachment (which itself
        // cascades with the source). ArtifactId stays a loose Guid — a second FK would
        // give SQL Server two cascade paths into the same row.
        builder.HasOne(p => p.Attachment)
            .WithMany()
            .HasForeignKey(p => p.SourceAttachmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // One pin per (map, artifact): re-extraction upserts instead of stacking pins.
        builder.HasIndex(p => new { p.SourceAttachmentId, p.ArtifactId })
            .IsUnique();

        builder.HasIndex(p => p.ArtifactId);

        builder.HasIndex(p => p.WorldId);
    }
}
