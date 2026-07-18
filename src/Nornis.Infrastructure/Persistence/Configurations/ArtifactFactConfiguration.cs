using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ArtifactFactConfiguration : IEntityTypeConfiguration<ArtifactFact>
{
    public void Configure(EntityTypeBuilder<ArtifactFact> builder)
    {
        builder.ToTable("ArtifactFacts");

        builder.HasKey(af => af.Id);

        builder.Property(af => af.Predicate)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(af => af.Value)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(af => af.TruthState)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(af => af.Visibility)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(af => af.Confidence)
            .HasPrecision(5, 4);

        builder.Property(af => af.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(af => af.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(af => af.RowVersion)
            .IsRowVersion();

        builder.HasOne(af => af.Artifact)
            .WithMany(a => a.ArtifactFacts)
            .HasForeignKey(af => af.ArtifactId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(af => af.CreatedByUser)
            .WithMany()
            .HasForeignKey(af => af.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
