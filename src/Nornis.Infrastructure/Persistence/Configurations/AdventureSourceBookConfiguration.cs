using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class AdventureSourceBookConfiguration : IEntityTypeConfiguration<AdventureSourceBook>
{
    public void Configure(EntityTypeBuilder<AdventureSourceBook> builder)
    {
        builder.ToTable("AdventureSourceBooks");

        builder.HasKey(b => new { b.AdventureId, b.LibraryDocumentId });

        // Restrict, because Worlds cascades to both Adventures and LibraryDocuments and SQL
        // Server refuses a second cascade path. AdventureRepository.DeleteAsync and the world
        // delete remove the rows themselves.
        builder.HasOne<Adventure>()
            .WithMany(a => a.SourceBooks)
            .HasForeignKey(b => b.AdventureId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade from the book: a Library document leaves by several paths (delete, a Bibliotaph
        // reconcile), and every one of them should drop the choice without knowing about Prep.
        builder.HasOne(b => b.LibraryDocument)
            .WithMany()
            .HasForeignKey(b => b.LibraryDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
