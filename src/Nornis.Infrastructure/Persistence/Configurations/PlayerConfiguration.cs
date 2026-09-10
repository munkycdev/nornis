using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(Player.MaxNameChars);

        builder.Property(p => p.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(p => p.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(p => p.WorldId);

        // One player per membership: the invariant every member has exactly one linked player
        // rests on. Filtered, because the unlinked players all share a null.
        builder.HasIndex(p => new { p.WorldId, p.WorldMemberId })
            .IsUnique()
            .HasFilter("[WorldMemberId] IS NOT NULL");

        // Players go with their world; characters go with their player (see
        // CharacterConfiguration). That is the one cascading path into Characters.
        builder.HasOne(p => p.World)
            .WithMany()
            .HasForeignKey(p => p.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // ClientSetNull (NO ACTION in SQL): a database SET NULL here would give Players a
        // second cascading path from Worlds (Worlds→WorldMembers→Players beside Worlds→Players),
        // which SQL Server rejects. Removing a member therefore unlinks its player explicitly —
        // WorldMemberRepository.RemoveAsync is the one place a membership is removed — and the
        // player stays at the table with every character it had.
        builder.HasOne(p => p.WorldMember)
            .WithOne(m => m.Player)
            .HasForeignKey<Player>(p => p.WorldMemberId)
            .OnDelete(DeleteBehavior.ClientSetNull);
    }
}
