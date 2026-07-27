using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class SourceReferenceConfiguration : IEntityTypeConfiguration<SourceReference>
{
    public void Configure(EntityTypeBuilder<SourceReference> builder)
    {
        builder.ToTable("SourceReferences");

        builder.HasKey(sr => sr.Id);

        builder.Property(sr => sr.TargetType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(sr => sr.Quote)
            .HasMaxLength(2000);

        builder.Property(sr => sr.Notes)
            .HasMaxLength(2000);

        builder.Property(sr => sr.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasOne(sr => sr.Source)
            .WithMany()
            .HasForeignKey(sr => sr.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Provenance is looked up by what it points AT far more often than by its source: every
        // accepted proposal reads references for its target, and removing an artifact reads them
        // once per fact and once per relationship. The only index here was EF's FK on SourceId,
        // so all of that was a clustered-index scan of what is the highest-cardinality table in
        // the schema — one row per accepted fact, relationship, artifact and proposal — which
        // means the cost grew with everything the world has ever learned.
        //
        // Deliberately TargetId alone, not (TargetId, TargetType). TargetType is an enum stored
        // as a string with no declared length, so it is nvarchar(max) — including it would force
        // EF to narrow the column to nvarchar(450) to make it indexable, and that ALTER is a
        // blocking table rewrite. Migrations here must stay additive because they run against the
        // live database before the new images go live.
        //
        // Nothing is lost by omitting it: TargetId is a Guid pointing at one fact, relationship,
        // artifact or proposal, so a seek returns a handful of rows and the TargetType filter is
        // free on top of that.
        builder.HasIndex(sr => sr.TargetId);
    }
}
