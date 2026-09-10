using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ShelfLinkConfiguration : IEntityTypeConfiguration<ShelfLink>
{
    public void Configure(EntityTypeBuilder<ShelfLink> builder)
    {
        builder.ToTable("ShelfLinks");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Code)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(l => l.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(l => l.RevokedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(l => l.LastUsedAt)
            .HasColumnType("datetimeoffset");

        // The anonymous shelf looks links up by code, which must be globally unique.
        builder.HasIndex(l => l.Code)
            .IsUnique();

        // One standing link per player: minting again revokes the old one first, and this is
        // the guard behind that intent. Filtered, because the revoked ones may pile up.
        builder.HasIndex(l => l.PlayerId)
            .IsUnique()
            .HasFilter("[RevokedAt] IS NULL")
            .HasDatabaseName("IX_ShelfLinks_PlayerId_Standing");

        // A link goes with its player — and so with the claim or GM link that deletes the
        // unlinked row (feature 25's merge). No WorldId column: the world is the player's, and
        // a copy here would be a second cascading path from Worlds, which SQL Server refuses.
        builder.HasOne(l => l.Player)
            .WithMany()
            .HasForeignKey(l => l.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict on the creator FK avoids a second cascade path into ShelfLinks.
        builder.HasOne(l => l.CreatedByUser)
            .WithMany()
            .HasForeignKey(l => l.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
