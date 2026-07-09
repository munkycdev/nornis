using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ReviewBatchConfiguration : IEntityTypeConfiguration<ReviewBatch>
{
    public void Configure(EntityTypeBuilder<ReviewBatch> builder)
    {
        builder.ToTable("ReviewBatches");

        builder.HasKey(rb => rb.Id);

        builder.Property(rb => rb.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(rb => rb.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(rb => rb.CompletedAt)
            .HasColumnType("datetimeoffset");

        builder.HasOne(rb => rb.World)
            .WithMany()
            .HasForeignKey(rb => rb.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rb => rb.Source)
            .WithMany()
            .HasForeignKey(rb => rb.SourceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
