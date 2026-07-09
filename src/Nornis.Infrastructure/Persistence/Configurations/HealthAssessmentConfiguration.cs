using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class HealthAssessmentConfiguration : IEntityTypeConfiguration<HealthAssessment>
{
    public void Configure(EntityTypeBuilder<HealthAssessment> builder)
    {
        builder.ToTable("HealthAssessments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Model)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.CreatedAt)
            .HasColumnType("datetimeoffset");

        // Latest-assessment lookups are always world-scoped and ordered by recency.
        builder.HasIndex(a => new { a.WorldId, a.CreatedAt });

        builder.HasOne(a => a.World)
            .WithMany()
            .HasForeignKey(a => a.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Findings)
            .WithOne(f => f.HealthAssessment)
            .HasForeignKey(f => f.HealthAssessmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
