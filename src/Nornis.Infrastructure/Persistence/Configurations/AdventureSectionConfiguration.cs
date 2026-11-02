using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class AdventureSectionConfiguration : IEntityTypeConfiguration<AdventureSection>
{
    public void Configure(EntityTypeBuilder<AdventureSection> builder)
    {
        builder.ToTable("AdventureSections");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.Title)
            .IsRequired()
            .HasMaxLength(200);

        // Bounded by PrepOptions.MaxSectionChars on every write, not by the column.
        builder.Property(s => s.BodyMarkdown)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        // The concurrency token for a GM save racing a draft: an UPDATE based on a stale value
        // matches no row, and the repository reports the conflict instead of overwriting.
        builder.Property(s => s.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .IsConcurrencyToken();

        builder.HasIndex(s => new { s.AdventureId, s.SortOrder });

        builder.HasOne(s => s.Adventure)
            .WithMany(a => a.Sections)
            .HasForeignKey(s => s.AdventureId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
