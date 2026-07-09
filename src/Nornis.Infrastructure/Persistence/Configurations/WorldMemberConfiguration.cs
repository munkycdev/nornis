using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class WorldMemberConfiguration : IEntityTypeConfiguration<WorldMember>
{
    public void Configure(EntityTypeBuilder<WorldMember> builder)
    {
        builder.ToTable("WorldMembers");

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

        builder.HasIndex(cm => new { cm.WorldId, cm.UserId })
            .IsUnique();

        builder.HasOne(cm => cm.World)
            .WithMany(c => c.WorldMembers)
            .HasForeignKey(cm => cm.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cm => cm.User)
            .WithMany()
            .HasForeignKey(cm => cm.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
