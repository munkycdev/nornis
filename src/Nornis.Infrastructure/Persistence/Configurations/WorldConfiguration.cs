using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class WorldConfiguration : IEntityTypeConfiguration<World>
{
    public void Configure(EntityTypeBuilder<World> builder)
    {
        builder.ToTable("Worlds");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Description)
            .HasMaxLength(2000);

        builder.Property(c => c.DailyAiBudgetUsd)
            .HasColumnType("decimal(6,2)");

        builder.Property(c => c.PublicAskMonthlyBudgetUsd)
            .HasColumnType("decimal(6,2)");

        builder.Property(c => c.GameSystem)
            .HasMaxLength(200);

        builder.Property(c => c.PublicSlug)
            .HasMaxLength(60);

        // One slug maps to one world; the filter lets many worlds have no slug.
        builder.HasIndex(c => c.PublicSlug)
            .IsUnique()
            .HasFilter("[PublicSlug] IS NOT NULL");

        builder.Property(c => c.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.RowVersion)
            .IsRowVersion();

        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not SetNull, and deliberately: Campaign.WorldId already cascades from
        // here, so a second cascade back would give SQL Server two paths between the same
        // two tables and it refuses the schema outright. CampaignService clears this pointer
        // before deleting the campaign it names — which it has to do anyway, since the same
        // clearing is what a campaign leaving Active needs.
        builder.HasOne(c => c.CurrentCampaign)
            .WithMany()
            .HasForeignKey(c => c.CurrentCampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
