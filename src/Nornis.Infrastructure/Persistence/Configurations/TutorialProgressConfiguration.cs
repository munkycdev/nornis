using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class TutorialProgressConfiguration : IEntityTypeConfiguration<TutorialProgress>
{
    public void Configure(EntityTypeBuilder<TutorialProgress> builder)
    {
        builder.ToTable("TutorialProgress");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.StepKey)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(p => p.CompletedAt)
            .HasColumnType("datetimeoffset");

        // A step completes once per user per world; the unique index makes concurrent
        // detection races harmless (the loser's insert fails and is swallowed).
        builder.HasIndex(p => new { p.UserId, p.WorldId, p.StepKey })
            .IsUnique();

        // Progress rows die with their world. No FK to User: users are never hard-deleted.
        builder.HasOne<World>()
            .WithMany()
            .HasForeignKey(p => p.WorldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
