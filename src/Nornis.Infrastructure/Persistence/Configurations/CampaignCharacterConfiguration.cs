using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class CampaignCharacterConfiguration : IEntityTypeConfiguration<CampaignCharacter>
{
    public void Configure(EntityTypeBuilder<CampaignCharacter> builder)
    {
        builder.ToTable("CampaignCharacters");

        builder.HasKey(cc => cc.Id);

        builder.Property(cc => cc.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(cc => new { cc.CampaignId, cc.CharacterId })
            .IsUnique();

        // Restrict (NO ACTION) to avoid a second cascade path from Worlds; campaign
        // deletion removes its assignments in the repository before the delete.
        builder.HasOne(cc => cc.Campaign)
            .WithMany(c => c.CampaignCharacters)
            .HasForeignKey(cc => cc.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cc => cc.Character)
            .WithMany(c => c.CampaignCharacters)
            .HasForeignKey(cc => cc.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
