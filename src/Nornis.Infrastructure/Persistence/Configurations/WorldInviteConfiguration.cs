using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class WorldInviteConfiguration : IEntityTypeConfiguration<WorldInvite>
{
    public void Configure(EntityTypeBuilder<WorldInvite> builder)
    {
        builder.ToTable("WorldInvites");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Code)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(i => i.Role)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(i => i.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(i => i.ExpiresAt)
            .HasColumnType("datetimeoffset");

        builder.Property(i => i.RevokedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(i => i.RowVersion)
            .IsRowVersion();

        // Redemption looks invites up by code, which must be globally unique.
        builder.HasIndex(i => i.Code)
            .IsUnique();

        builder.HasIndex(i => i.WorldId);

        builder.HasOne(i => i.World)
            .WithMany()
            .HasForeignKey(i => i.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict on the creator FK avoids a second cascade path into WorldInvites.
        builder.HasOne(i => i.CreatedByUser)
            .WithMany()
            .HasForeignKey(i => i.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
