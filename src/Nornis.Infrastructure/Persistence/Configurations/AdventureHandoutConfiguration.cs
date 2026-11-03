using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

/// <summary>An Adventure's handouts (feature 38): the same shape and delete rules as its source books.</summary>
public class AdventureHandoutConfiguration : IEntityTypeConfiguration<AdventureHandout>
{
    public void Configure(EntityTypeBuilder<AdventureHandout> builder)
    {
        builder.ToTable("AdventureHandouts");

        builder.HasKey(h => new { h.AdventureId, h.LibraryDocumentId });

        // Restrict, because Worlds cascades to both Adventures and LibraryDocuments and SQL
        // Server refuses a second cascade path. AdventureRepository.DeleteAsync and the world
        // delete remove the rows themselves.
        builder.HasOne<Adventure>()
            .WithMany(a => a.Handouts)
            .HasForeignKey(h => h.AdventureId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade from the document: however it leaves the Library, it leaves the booklet too.
        builder.HasOne(h => h.LibraryDocument)
            .WithMany()
            .HasForeignKey(h => h.LibraryDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
