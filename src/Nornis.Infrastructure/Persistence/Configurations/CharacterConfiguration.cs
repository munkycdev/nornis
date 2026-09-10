using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        builder.ToTable("Characters");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Description)
            .HasMaxLength(2000);

        builder.Property(c => c.Sheet)
            .HasMaxLength(Character.MaxSheetChars);

        builder.Property(c => c.SheetUpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(c => c.WorldId);

        // Restrict (NO ACTION) to avoid a second cascade path from Worlds; characters
        // are removed through the Player cascade (Worlds→Players→Characters), which always
        // covers them.
        builder.HasOne(c => c.World)
            .WithMany()
            .HasForeignKey(c => c.WorldId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.PlayerId);

        builder.HasOne(c => c.Player)
            .WithMany(p => p.Characters)
            .HasForeignKey(c => c.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.ArtifactId);

        // ClientSetNull (NO ACTION in SQL) — a SET NULL here would add a second cascade
        // path into Characters (Worlds→Artifacts→Characters vs Worlds→Players→Characters),
        // which SQL Server rejects. Artifacts are never hard-deleted (merge archives them),
        // so the constraint never fires in practice.
        builder.HasOne(c => c.Artifact)
            .WithMany()
            .HasForeignKey(c => c.ArtifactId)
            .OnDelete(DeleteBehavior.ClientSetNull);
    }
}
