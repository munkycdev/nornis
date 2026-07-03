using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ArtifactRelationshipConfiguration : IEntityTypeConfiguration<ArtifactRelationship>
{
    public void Configure(EntityTypeBuilder<ArtifactRelationship> builder)
    {
        builder.ToTable("ArtifactRelationships");

        builder.HasKey(ar => ar.Id);

        builder.Property(ar => ar.Type)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(ar => ar.Description)
            .HasMaxLength(2000);

        builder.Property(ar => ar.TruthState)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(ar => ar.Visibility)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(ar => ar.Confidence)
            .HasPrecision(5, 4);

        builder.Property(ar => ar.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(ar => ar.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(ar => ar.RowVersion)
            .IsRowVersion();

        builder.HasIndex(ar => ar.ArtifactAId);

        builder.HasIndex(ar => ar.ArtifactBId);

        builder.HasOne(ar => ar.Campaign)
            .WithMany()
            .HasForeignKey(ar => ar.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ar => ar.ArtifactA)
            .WithMany()
            .HasForeignKey(ar => ar.ArtifactAId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ar => ar.ArtifactB)
            .WithMany()
            .HasForeignKey(ar => ar.ArtifactBId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
