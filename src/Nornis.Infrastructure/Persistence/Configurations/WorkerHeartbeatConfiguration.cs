using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class WorkerHeartbeatConfiguration : IEntityTypeConfiguration<WorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<WorkerHeartbeat> builder)
    {
        builder.ToTable("WorkerHeartbeats");

        // The host name is the key, so a scaled-out worker keeps overwriting one row
        // instead of accumulating one per replica per restart.
        builder.HasKey(h => h.WorkerName);

        builder.Property(h => h.WorkerName)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(h => h.BeatAt)
            .HasColumnType("datetimeoffset");
    }
}
