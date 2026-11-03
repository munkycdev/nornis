using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class AdventureConfiguration : IEntityTypeConfiguration<Adventure>
{
    public void Configure(EntityTypeBuilder<Adventure> builder)
    {
        builder.ToTable("Adventures");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(200);

        // Assigned once by SlugAssigner from the title; unique per world, null only until backfilled.
        builder.Property(a => a.Slug)
            .HasMaxLength(Slug.MaxStoredLength);

        builder.HasIndex(a => new { a.WorldId, a.Slug })
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL");

        builder.Property(a => a.Pitch)
            .HasMaxLength(4000);

        // Four nullable Details_* columns on Adventures. Every Adventure has details (all four
        // null means none set), so the navigation is required: an optional owned type made only of
        // nullable columns is one EF can't tell apart from "absent", and warns about.
        builder.OwnsOne(a => a.Details, details =>
        {
            details.Property(d => d.GameSystem).HasMaxLength(AdventureDetails.MaxGameSystemLength);
            details.Property(d => d.Edition).HasMaxLength(AdventureDetails.MaxEditionLength);
            details.Property(d => d.Levels).HasMaxLength(AdventureDetails.MaxLevelsLength);
        });

        builder.Navigation(a => a.Details).IsRequired();

        // Feature 38: three bool columns (the migration adds them as false). The recorded two are only ever set
        // to true, by AdventureRepository's AI writes; UpdateAsync copies the GM's Declared only.
        builder.OwnsOne(a => a.AiUse, ai =>
        {
            ai.Property(u => u.WroteText).HasColumnName("AiWroteText");
            ai.Property(u => u.MadeArt).HasColumnName("AiMadeArt");
            ai.Property(u => u.Declared).HasColumnName("AiDeclared");
        });

        builder.Navigation(a => a.AiUse).IsRequired();

        // Feature 38: the cover art, six nullable Cover_* columns. Optional: BlobPath is required
        // inside, so an Adventure with no art reads back with a null Cover.
        builder.OwnsOne(a => a.Cover, cover =>
        {
            cover.Property(c => c.BlobPath).IsRequired().HasMaxLength(500);
            cover.Property(c => c.ContentType).IsRequired().HasMaxLength(50);
            cover.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);
            cover.Property(c => c.Brief).HasMaxLength(AdventureCover.MaxBriefChars);
            cover.Property(c => c.Layout).HasConversion<string>().HasMaxLength(20);
            cover.Property(c => c.UpdatedAt).HasColumnType("datetimeoffset");
        });

        builder.Property(a => a.CoverBrief).HasMaxLength(AdventureCover.MaxBriefChars);

        // A JSON array of ids: at most the image cap's worth, never queried into.
        builder.Property(a => a.CoverOptionIds).HasMaxLength(4000);

        // The design draft (feature 37). Its stamp is a concurrency token, so two tabs (or a tab
        // and an applied edit) can't silently overwrite each other's draft.
        builder.Property(a => a.DraftMarkdown)
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.DraftUpdatedAt)
            .HasColumnType("datetimeoffset")
            .IsConcurrencyToken();

        builder.Property(a => a.TableBrief)
            .HasMaxLength(Adventure.MaxTableBriefChars);

        builder.Property(a => a.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(a => a.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(a => a.WorldId);

        builder.HasOne(a => a.World)
            .WithMany()
            .HasForeignKey(a => a.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict for the reason Source.CampaignId is: Worlds already cascades to Campaigns,
        // and SQL Server refuses a second path. CampaignRepository.DeleteAsync detaches first.
        builder.HasOne(a => a.Campaign)
            .WithMany()
            .HasForeignKey(a => a.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict for the same reason; LibraryDocumentRepository.DeleteAsync detaches first.
        builder.HasOne(a => a.LastPrintedDocument)
            .WithMany()
            .HasForeignKey(a => a.LastPrintedDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.CreatedByUser)
            .WithMany()
            .HasForeignKey(a => a.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
