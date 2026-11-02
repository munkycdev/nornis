using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class BibliotaphWorldEventConfiguration : IEntityTypeConfiguration<BibliotaphWorldEvent>
{
    public void Configure(EntityTypeBuilder<BibliotaphWorldEvent> builder)
    {
        builder.ToTable("BibliotaphWorldEvents");

        builder.HasKey(e => e.Id);

        // Ids are minted by Nornis when the event is queued; they are Bibliotaph's idempotency key.
        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.WorldName)
            .IsRequired()
            .HasMaxLength(BibliotaphWorldEvent.MaxWorldNameLength);

        builder.Property(e => e.LastError)
            .HasMaxLength(200);

        builder.Property(e => e.OccurredAt)
            .HasColumnType("datetimeoffset");

        builder.Property(e => e.NextAttemptAt)
            .HasColumnType("datetimeoffset");

        builder.Property(e => e.DeliveredAt)
            .HasColumnType("datetimeoffset");

        builder.Property(e => e.FailedAt)
            .HasColumnType("datetimeoffset");

        // The Worker's poll: undelivered, not failed, due.
        builder.HasIndex(e => new { e.DeliveredAt, e.FailedAt, e.NextAttemptAt });

        builder.HasIndex(e => new { e.UserId, e.OccurredAt });

        // No foreign key on WorldId, on purpose: a world's deletion must not take its own
        // `deleted` event with it. The creator's FK cascades; there is no user delete path.
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
