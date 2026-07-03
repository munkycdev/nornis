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

        builder.HasOne<Campaign>()
            .WithMany()
            .HasForeignKey(a => a.CampaignId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

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
