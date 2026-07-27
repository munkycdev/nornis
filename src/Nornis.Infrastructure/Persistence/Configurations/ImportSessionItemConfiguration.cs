using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ImportSessionItemConfiguration : IEntityTypeConfiguration<ImportSessionItem>
{
    public void Configure(EntityTypeBuilder<ImportSessionItem> builder)
    {
        builder.ToTable("ImportSessionItems");

        builder.HasKey(i => i.Id);

        // Every read of a session walks its items in order.
        builder.HasIndex(i => new { i.ImportSessionId, i.Position });

        // Rows written before staging existing sources existed were all created by the
        // add-note form, so the safe default preserves their delete-with-item behaviour.
        builder.Property(i => i.CreatedByImport)
            .HasDefaultValue(true);

        builder.HasOne(i => i.ImportSession)
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.ImportSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // SourceId is deliberately a loose reference (no FK): the session already cascades
        // from World, and a Source FK would give SQL Server two cascade paths to this table
        // — the same reason ExtractionReplay.CurrentSourceId is loose.
    }
}
