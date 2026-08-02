using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddExtractionBatchUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_ReviewBatches_SourceId_Extraction",
            table: "ReviewBatches",
            column: "SourceId",
            unique: true,
            filter: "[Kind] IS NULL AND [Status] IN ('Pending', 'InReview', 'Completed')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ReviewBatches_SourceId_Extraction",
            table: "ReviewBatches");
    }
}
