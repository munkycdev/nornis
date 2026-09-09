using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class CharacterSheetSnapshotConfiguration : IEntityTypeConfiguration<CharacterSheetSnapshot>
{
    public void Configure(EntityTypeBuilder<CharacterSheetSnapshot> builder)
    {
        builder.ToTable("CharacterSheetSnapshots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Note)
            .HasMaxLength(500);

        builder.Property(s => s.AsOf)
            .HasColumnType("datetimeoffset");

        builder.Property(s => s.CreatedAt)
            .HasColumnType("datetimeoffset");

        // Attaching the same source twice is the same statement made twice.
        builder.HasIndex(s => new { s.CharacterId, s.SourceId })
            .IsUnique();

        // Worlds→Sources→Snapshots and Worlds→Members→Characters→Snapshots are two cascade
        // paths between the same pair of tables, which SQL Server refuses. Source keeps the
        // cascade because "deleting a source must not leave a dangling attachment" is a
        // requirement worth having the database enforce; the character side is cleaned up
        // explicitly by CharacterRepository.DeleteAsync, the same way CampaignRepository
        // already detaches what it cannot cascade.
        builder.HasOne(s => s.Source)
            .WithMany()
            .HasForeignKey(s => s.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Character)
            .WithMany()
            .HasForeignKey(s => s.CharacterId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
