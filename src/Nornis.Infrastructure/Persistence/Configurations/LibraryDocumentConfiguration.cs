using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class LibraryDocumentConfiguration : IEntityTypeConfiguration<LibraryDocument>
{
    public void Configure(EntityTypeBuilder<LibraryDocument> builder)
    {
        builder.ToTable("LibraryDocuments");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.FileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(d => d.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.BlobPath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(d => d.Kind)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(d => d.Visibility)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(d => d.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(d => d.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(d => d.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(d => d.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(d => new { d.WorldId, d.Status });

        builder.HasOne(d => d.World)
            .WithMany()
            .HasForeignKey(d => d.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.UploadedByUser)
            .WithMany()
            .HasForeignKey(d => d.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
