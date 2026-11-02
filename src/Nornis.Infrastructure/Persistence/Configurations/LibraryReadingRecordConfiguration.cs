using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class LibraryReadingRecordConfiguration : IEntityTypeConfiguration<LibraryReadingRecord>
{
    public void Configure(EntityTypeBuilder<LibraryReadingRecord> builder)
    {
        builder.ToTable("LibraryReadingRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ViewerName)
            .IsRequired()
            .HasMaxLength(LibraryReadingRecord.MaxViewerNameLength);

        builder.Property(r => r.OpenedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(r => new { r.LibraryDocumentId, r.OpenedAt });

        // For the 90-day purge.
        builder.HasIndex(r => r.OpenedAt);

        // The one foreign key: a record goes with its document, and so with its world, whose
        // deletion removes the documents. WorldId, UserId and PlayerId are plain columns, so a
        // log row never blocks a player's claim or removal. LibraryDocumentRepository.DeleteAsync
        // and the world graph delete these explicitly as well, as nothing there relies on cascades.
        builder.HasOne<LibraryDocument>()
            .WithMany()
            .HasForeignKey(r => r.LibraryDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
