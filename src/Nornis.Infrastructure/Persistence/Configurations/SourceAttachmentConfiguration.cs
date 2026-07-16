using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class SourceAttachmentConfiguration : IEntityTypeConfiguration<SourceAttachment>
{
    public void Configure(EntityTypeBuilder<SourceAttachment> builder)
    {
        builder.ToTable("SourceAttachments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.BlobPath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(a => a.Kind)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(a => a.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(a => a.SourceId);

        builder.HasOne(a => a.Source)
            .WithMany()
            .HasForeignKey(a => a.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
