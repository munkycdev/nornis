using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContinuityDismissalRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContinuityDismissals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DismissedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContinuityDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContinuityDismissals_Worlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "Worlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContinuityDismissals_WorldId",
                table: "ContinuityDismissals",
                column: "WorldId");

            // Backfill the registry from every finding a GM has ever dismissed, so historical
            // adjudications survive — including the ones the old one-generation carry-forward
            // already lost. Additive: this only inserts into the table created above.
            //
            // Duplicates are possible (the same issue dismissed in several generations) and
            // harmless: matching is "any registry row matches", not a lookup by key. No
            // dedupe here because SQL Server cannot GROUP BY or reliably DISTINCT an
            // nvarchar(max) column, and the row count is bounded by dismissals to date.
            migrationBuilder.Sql("""
                INSERT INTO [ContinuityDismissals] ([Id], [WorldId], [Category], [EvidenceJson], [DismissedAtUtc])
                SELECT NEWID(), [ha].[WorldId], [cf].[Category], [cf].[EvidenceJson], [ha].[CreatedAt]
                FROM [ContinuityFindings] AS [cf]
                INNER JOIN [HealthAssessments] AS [ha] ON [ha].[Id] = [cf].[HealthAssessmentId]
                WHERE [cf].[Status] = 'Dismissed';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContinuityDismissals");
        }
    }
}
