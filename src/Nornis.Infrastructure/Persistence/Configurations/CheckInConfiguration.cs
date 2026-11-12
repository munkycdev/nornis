using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class CheckInConfiguration : IEntityTypeConfiguration<CheckIn>
{
    public void Configure(EntityTypeBuilder<CheckIn> builder)
    {
        builder.ToTable("CheckIns");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Model)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.LatestSessionAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.ClosedAt)
            .HasColumnType("datetimeoffset");

        // Read per world: the open one (ClosedAt null) on every Home load, the newest few when a
        // check-in starts.
        builder.HasIndex(c => new { c.WorldId, c.CreatedAt });

        builder.HasOne(c => c.World)
            .WithMany()
            .HasForeignKey(c => c.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Questions)
            .WithOne(q => q.CheckIn)
            .HasForeignKey(q => q.CheckInId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
