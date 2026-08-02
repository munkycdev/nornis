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

        builder.Property(rb => rb.Kind)
            .HasMaxLength(40);

        builder.Property(rb => rb.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(rb => rb.CompletedAt)
            .HasColumnType("datetimeoffset");

        // One extraction batch per source, enforced where two workers cannot both pass it.
        // Idempotency was a read of GetBySourceIdAsync followed by an unconditional write, so a
        // redelivery that crossed the lock-renewal ceiling ran a second full pass concurrently
        // with the first: two batches, twice the proposals, twice the AI spend.
        //
        // The filter is GetBySourceIdAsync's predicate, character for character, and it has to
        // be: an index that forbids more than the query treats as blocking would forbid
        // reprocessing after a Canceled batch, which that method allows on purpose. Kind IS NULL
        // is what "extraction batch" means; merge and retrospective batches are also Kind-null
        // but each mints its own synthetic source, so they hold one row apiece and never meet
        // this filter twice.
        //
        // Verified against production before shipping — zero sources held two — because a unique
        // index added over existing duplicates fails its migration and takes the deploy with it.
        builder.HasIndex(rb => rb.SourceId, "IX_ReviewBatches_SourceId_Extraction")
            .IsUnique()
            .HasFilter("[Kind] IS NULL AND [Status] IN ('Pending', 'InReview', 'Completed')");

        // Declared explicitly only so the new index above does not replace it. EF creates this
        // one implicitly for the SourceId foreign key, and the first generated migration
        // dropped it — leaving the FK's own delete-time check, and every lookup of a merge or
        // reveal batch, without an index the filtered one cannot serve.
        builder.HasIndex(rb => rb.SourceId, "IX_ReviewBatches_SourceId");

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
