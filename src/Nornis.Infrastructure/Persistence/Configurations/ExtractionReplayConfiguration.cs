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

        // …and the uniqueness that makes the service's check-then-create safe. Without it a
        // double-click produced two Active replays for one world: advance and cancel then
        // targeted an arbitrary one of them and both requeued sources. ImportSessions two
        // files away already enforced exactly this invariant — this is that pattern, not a
        // new idea. Status is stored as a string (see the conversion above), so the filter
        // matches on the name.
        builder.HasIndex(r => r.WorldId)
            .IsUnique()
            .HasFilter("[Status] = 'Active'")
            .HasDatabaseName("IX_ExtractionReplays_WorldId_Active");

        builder.HasOne(r => r.World)
            .WithMany()
            .HasForeignKey(r => r.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // CurrentSourceId is deliberately a loose reference (no FK): a Source FK alongside
        // the World cascade would create competing cascade paths, the same reason
        // MapPlacemark.ArtifactId is loose.
    }
}
