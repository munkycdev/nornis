using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;

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

        // Assigned once by SlugAssigner from the title; unique per world, null only until backfilled.
        builder.Property(d => d.Slug)
            .HasMaxLength(Slug.MaxStoredLength);

        builder.HasIndex(d => new { d.WorldId, d.Slug })
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL");

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

        builder.Property(d => d.Origin)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // Bibliotaph's ids are opaque strings; see the Bibliotaph integration spec.
        builder.Property(d => d.BibliotaphEntryId)
            .HasMaxLength(64);

        builder.Property(d => d.BibliotaphDocumentId)
            .HasMaxLength(64);

        builder.Property(d => d.RemoteTitle)
            .HasMaxLength(200);

        builder.Property(d => d.CoverName)
            .HasMaxLength(200);

        builder.Property(d => d.RemovedAt)
            .HasColumnType("datetimeoffset");

        // An entry appears once per world: the reconcile matches on it.
        builder.HasIndex(d => new { d.WorldId, d.BibliotaphEntryId })
            .IsUnique()
            .HasFilter("[BibliotaphEntryId] IS NOT NULL");

        builder.Property(d => d.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(d => d.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(d => new { d.WorldId, d.Status });

        builder.HasIndex(d => d.AdventureId);

        // Ten nullable PrintDetails_* columns. Optional: a document that isn't a print has all
        // ten null and reads back with no print details. Title, Type, Publisher and Author are
        // required inside, so a half-written snapshot can't load as a print.
        builder.OwnsOne(d => d.PrintDetails, print =>
        {
            print.Property(p => p.Title).IsRequired().HasMaxLength(PrintDetails.MaxTitleLength);
            print.Property(p => p.GameSystem).HasMaxLength(AdventureDetails.MaxGameSystemLength);
            print.Property(p => p.Edition).HasMaxLength(AdventureDetails.MaxEditionLength);
            print.Property(p => p.Type).IsRequired().HasMaxLength(PrintDetails.MaxFixedLength);
            print.Property(p => p.Levels).HasMaxLength(AdventureDetails.MaxLevelsLength);
            print.Property(p => p.Publisher).IsRequired().HasMaxLength(PrintDetails.MaxFixedLength);
            print.Property(p => p.Author).IsRequired().HasMaxLength(PrintDetails.MaxAuthorLength);

            // Feature 38. Null on prints filed before it: unknown, not "none".
            print.Property(p => p.AiUse).HasConversion<string>().HasMaxLength(20);

            // Feature 40. Null on prints filed before it, read as false.
            print.Property(p => p.ResearchedOnWeb);
        });

        // A Prep print's Adventure. Restrict (Worlds cascades to both tables);
        // AdventureRepository.DeleteAsync detaches the prints, which outlive their Adventure.
        builder.HasOne<Adventure>()
            .WithMany()
            .HasForeignKey(d => d.AdventureId)
            .OnDelete(DeleteBehavior.Restrict);

        // The Adventure an upload belongs to (feature 36). Restrict for the same reason as a
        // print's: Worlds cascades to both tables, so SQL Server refuses a second cascade path.
        // AdventureRepository.DeleteAsync lets go of these too, returning them to the Shelf.
        builder.HasOne<Adventure>()
            .WithMany()
            .HasForeignKey(d => d.LinkedAdventureId)
            .OnDelete(DeleteBehavior.Restrict);

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
