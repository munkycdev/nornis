using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ContinuityDismissalConfiguration : IEntityTypeConfiguration<ContinuityDismissal>
{
    public void Configure(EntityTypeBuilder<ContinuityDismissal> builder)
    {
        builder.ToTable("ContinuityDismissals");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Category)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(d => d.EvidenceJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(d => d.DismissedAtUtc)
            .HasColumnType("datetimeoffset");

        // The registry is always read whole, per world, on every assessment run.
        builder.HasIndex(d => d.WorldId);

        builder.HasOne(d => d.World)
            .WithMany()
            .HasForeignKey(d => d.WorldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
