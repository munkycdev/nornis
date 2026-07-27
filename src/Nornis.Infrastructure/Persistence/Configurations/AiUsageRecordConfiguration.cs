using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.ToTable("AiUsageRecords");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Model)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.ErrorCode)
            .HasMaxLength(200);

        builder.Property(a => a.EstimatedCostUsd)
            .HasPrecision(18, 8);

        builder.Property(a => a.OperationType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.CreatedAt)
            .HasColumnType("datetimeoffset");

        // The budget guard aggregates spend for a world over a date range before EVERY AI call,
        // and the public-Ask cap does the same per anonymous question — so this is the hottest
        // read on an append-only ledger that never shrinks. EF's FK index on WorldId alone stops
        // being selective once a world has months of history, degenerating into a range scan of
        // everything that world has ever spent. The composite turns each check into a seek.
        builder.HasIndex(a => new { a.WorldId, a.CreatedAt });

        // Cost-ledger records outlive the world/user they reference: when a World or
        // User is deleted, null the FK rather than deleting the historical usage record.
        builder.HasOne<World>()
            .WithMany()
            .HasForeignKey(a => a.WorldId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(a => a.SourceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ReviewBatch>()
            .WithMany()
            .HasForeignKey(a => a.ReviewBatchId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
