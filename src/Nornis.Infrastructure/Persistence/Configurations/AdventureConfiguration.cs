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
