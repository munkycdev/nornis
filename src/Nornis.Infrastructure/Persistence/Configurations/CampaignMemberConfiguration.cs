using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class CampaignMemberConfiguration : IEntityTypeConfiguration<CampaignMember>
{
    public void Configure(EntityTypeBuilder<CampaignMember> builder)
    {
        builder.ToTable("CampaignMembers");

        builder.HasKey(cm => cm.Id);

        builder.Property(cm => cm.Role)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(cm => cm.DisplayName)
            .HasMaxLength(200);

        builder.Property(cm => cm.CharacterName)
            .HasMaxLength(200);

        builder.Property(cm => cm.JoinedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(cm => new { cm.CampaignId, cm.UserId })
            .IsUnique();

        builder.HasOne(cm => cm.Campaign)
            .WithMany(c => c.CampaignMembers)
            .HasForeignKey(cm => cm.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cm => cm.User)
            .WithMany()
            .HasForeignKey(cm => cm.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
