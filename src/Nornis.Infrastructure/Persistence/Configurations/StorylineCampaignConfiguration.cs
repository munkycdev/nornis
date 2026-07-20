using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class StorylineCampaignConfiguration : IEntityTypeConfiguration<StorylineCampaign>
{
    public void Configure(EntityTypeBuilder<StorylineCampaign> builder)
    {
        builder.ToTable("StorylineCampaigns");

        builder.HasKey(sc => sc.Id);

        builder.Property(sc => sc.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(sc => new { sc.ArtifactId, sc.CampaignId })
            .IsUnique();

        // Restrict (NO ACTION) on the campaign side to avoid a second cascade path from
        // Worlds; campaign deletion sheds its declarations in the repository before the delete.
        builder.HasOne(sc => sc.Campaign)
            .WithMany(c => c.StorylineCampaigns)
            .HasForeignKey(sc => sc.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        // The artifact is the single cascade path: removing a storyline from canon takes its
        // declarations with it.
        builder.HasOne(sc => sc.Artifact)
            .WithMany()
            .HasForeignKey(sc => sc.ArtifactId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
