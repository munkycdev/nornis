using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class SourceExtractionConfiguration : IEntityTypeConfiguration<SourceExtraction>
{
    public void Configure(EntityTypeBuilder<SourceExtraction> builder)
    {
        builder.ToTable("SourceExtractions");

        builder.HasKey(se => se.Id);

        builder.Property(se => se.Text)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(se => se.ExtractionType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(se => se.Confidence)
            .HasPrecision(5, 4);

        builder.Property(se => se.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasOne<Source>()
            .WithMany(s => s.SourceExtractions)
            .HasForeignKey(se => se.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
