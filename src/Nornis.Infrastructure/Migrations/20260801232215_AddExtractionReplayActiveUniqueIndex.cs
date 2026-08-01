using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddExtractionReplayActiveUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_ExtractionReplays_WorldId_Active",
            table: "ExtractionReplays",
            column: "WorldId",
            unique: true,
            filter: "[Status] = 'Active'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ExtractionReplays_WorldId_Active",
            table: "ExtractionReplays");
    }
}
