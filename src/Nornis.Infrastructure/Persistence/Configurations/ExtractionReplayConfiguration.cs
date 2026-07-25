using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ExtractionReplayConfiguration : IEntityTypeConfiguration<ExtractionReplay>
{
    public void Configure(EntityTypeBuilder<ExtractionReplay> builder)
    {
        builder.ToTable("ExtractionReplays");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(r => r.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(r => r.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(r => r.CompletedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(r => r.RowVersion)
            .IsRowVersion();

        // The one query that matters: "this world's active replay".
        builder.HasIndex(r => new { r.WorldId, r.Status });

        builder.HasOne(r => r.World)
            .WithMany()
            .HasForeignKey(r => r.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // CurrentSourceId is deliberately a loose reference (no FK): a Source FK alongside
        // the World cascade would create competing cascade paths, the same reason
        // MapPlacemark.ArtifactId is loose.
    }
}
