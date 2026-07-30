using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddProvenanceAndUsageIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Provenance is queried by what it points at — every accepted proposal, and every
        // fact and relationship during an artifact removal. Until now the only index was
        // EF's FK on SourceId, so all of that scanned the largest table in the schema.
        migrationBuilder.CreateIndex(
            name: "IX_SourceReferences_TargetId",
            table: "SourceReferences",
            column: "TargetId");

        // The budget guard aggregates a world's spend over a date range before every AI
        // call, which the WorldId-only index cannot serve once a world has real history.
        migrationBuilder.CreateIndex(
            name: "IX_AiUsageRecords_WorldId_CreatedAt",
            table: "AiUsageRecords",
            columns: new[] { "WorldId", "CreatedAt" });

        // Created BEFORE this drop, deliberately — EF scaffolds the drop first, which would
        // leave WorldId lookups unindexed for however long the composite takes to build. The
        // composite makes the single-column FK index redundant (same leading column), so
        // dropping it removes write amplification without costing any read.
        // This migration runs against the live database while the previous image is still
        // serving; no data is touched either way.
        migrationBuilder.DropIndex(
            name: "IX_AiUsageRecords_WorldId",
            table: "AiUsageRecords");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SourceReferences_TargetId",
            table: "SourceReferences");

        migrationBuilder.DropIndex(
            name: "IX_AiUsageRecords_WorldId_CreatedAt",
            table: "AiUsageRecords");

        migrationBuilder.CreateIndex(
            name: "IX_AiUsageRecords_WorldId",
            table: "AiUsageRecords",
            column: "WorldId");
    }
}
