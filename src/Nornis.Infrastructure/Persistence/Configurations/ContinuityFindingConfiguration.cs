using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ContinuityFindingConfiguration : IEntityTypeConfiguration<ContinuityFinding>
{
    public void Configure(EntityTypeBuilder<ContinuityFinding> builder)
    {
        builder.ToTable("ContinuityFindings");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Category)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(f => f.Severity)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(f => f.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(f => f.Summary)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(f => f.SuggestedAction)
            .HasMaxLength(1000);

        builder.Property(f => f.EvidenceJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(f => new { f.HealthAssessmentId, f.Status });

        // The finding's primary artifact is a soft navigation hint, not an owning relationship —
        // no FK, so deleting an artifact never cascades into historical findings.
    }
}
