using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ReviewProposalConfiguration : IEntityTypeConfiguration<ReviewProposal>
{
    public void Configure(EntityTypeBuilder<ReviewProposal> builder)
    {
        builder.ToTable("ReviewProposals");

        builder.HasKey(rp => rp.Id);

        builder.Property(rp => rp.ProposedValueJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(rp => rp.Rationale)
            .HasMaxLength(2000);

        builder.Property(rp => rp.ChangeType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(rp => rp.TargetType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(rp => rp.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(rp => rp.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(rp => rp.ReviewedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(rp => rp.RowVersion)
            .IsRowVersion();

        builder.HasIndex(rp => new { rp.ReviewBatchId, rp.Status });

        builder.HasOne(rp => rp.ReviewBatch)
            .WithMany(rb => rb.ReviewProposals)
            .HasForeignKey(rp => rp.ReviewBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(rp => rp.ReviewedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
