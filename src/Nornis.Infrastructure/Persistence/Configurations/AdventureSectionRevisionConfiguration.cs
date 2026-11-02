using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class AdventureSectionRevisionConfiguration : IEntityTypeConfiguration<AdventureSectionRevision>
{
    public void Configure(EntityTypeBuilder<AdventureSectionRevision> builder)
    {
        builder.ToTable("AdventureSectionRevisions");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.BodyMarkdown)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(r => r.Instruction)
            .HasMaxLength(4000);

        builder.Property(r => r.Model)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.CreatedAt)
            .HasColumnType("datetimeoffset");

        // Undo reads the newest; the trim deletes the oldest past the bound.
        builder.HasIndex(r => new { r.SectionId, r.CreatedAt });

        builder.HasOne(r => r.Section)
            .WithMany(s => s.Revisions)
            .HasForeignKey(r => r.SectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
