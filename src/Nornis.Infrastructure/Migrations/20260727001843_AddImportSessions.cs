using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddImportSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ImportSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_ImportSessions_Worlds_WorldId",
                    column: x => x.WorldId,
                    principalTable: "Worlds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ImportSessionItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ImportSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Position = table.Column<int>(type: "int", nullable: false),
                Skipped = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportSessionItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_ImportSessionItems_ImportSessions_ImportSessionId",
                    column: x => x.ImportSessionId,
                    principalTable: "ImportSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ImportSessionItems_ImportSessionId_Position",
            table: "ImportSessionItems",
            columns: new[] { "ImportSessionId", "Position" });

        migrationBuilder.CreateIndex(
            name: "IX_ImportSessions_WorldId_NonTerminal",
            table: "ImportSessions",
            column: "WorldId",
            unique: true,
            filter: "[Status] IN ('Draft', 'InProgress')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ImportSessionItems");

        migrationBuilder.DropTable(
            name: "ImportSessions");
    }
}
