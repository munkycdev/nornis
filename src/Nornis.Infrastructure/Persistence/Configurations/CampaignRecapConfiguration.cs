using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class CampaignRecapConfiguration : IEntityTypeConfiguration<CampaignRecap>
{
    public void Configure(EntityTypeBuilder<CampaignRecap> builder)
    {
        builder.ToTable("CampaignRecaps");

        builder.HasKey(r => r.Id);

        // One recap per campaign — the row is the record; UpsertAsync replaces it in place
        // and the index makes a concurrent-insert race a constraint violation instead of a
        // silent second row.
        builder.HasIndex(r => r.CampaignId)
            .IsUnique();

        builder.Property(r => r.Model)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.GeneratedAt)
            .HasColumnType("datetimeoffset");

        // Restrict (NO ACTION) to avoid a second cascade path from Worlds; campaign
        // deletion removes the recap in the repository before the delete.
        builder.HasOne(r => r.Campaign)
            .WithMany()
            .HasForeignKey(r => r.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
