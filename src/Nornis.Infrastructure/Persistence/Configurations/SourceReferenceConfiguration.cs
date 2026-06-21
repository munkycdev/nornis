using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class SourceReferenceConfiguration : IEntityTypeConfiguration<SourceReference>
{
    public void Configure(EntityTypeBuilder<SourceReference> builder)
    {
        builder.ToTable("SourceReferences");

        builder.HasKey(sr => sr.Id);

        builder.Property(sr => sr.TargetType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(sr => sr.Quote)
            .HasMaxLength(2000);

        builder.Property(sr => sr.Notes)
            .HasMaxLength(2000);

        builder.Property(sr => sr.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasOne(sr => sr.Source)
            .WithMany()
            .HasForeignKey(sr => sr.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
