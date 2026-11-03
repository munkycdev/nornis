using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverRightsHandouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrintDetails_AiUse",
                table: "LibraryDocuments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiDeclared",
                table: "Adventures",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiMadeArt",
                table: "Adventures",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiWroteText",
                table: "Adventures",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CoverBrief",
                table: "Adventures",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoverImagesGenerated",
                table: "Adventures",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CoverOptionIds",
                table: "Adventures",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Cover_BlobPath",
                table: "Adventures",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cover_Brief",
                table: "Adventures",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cover_ContentType",
                table: "Adventures",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cover_Layout",
                table: "Adventures",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cover_Source",
                table: "Adventures",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Cover_UpdatedAt",
                table: "Adventures",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdventureHandouts",
                columns: table => new
                {
                    AdventureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LibraryDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureHandouts", x => new { x.AdventureId, x.LibraryDocumentId });
                    table.ForeignKey(
                        name: "FK_AdventureHandouts_Adventures_AdventureId",
                        column: x => x.AdventureId,
                        principalTable: "Adventures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdventureHandouts_LibraryDocuments_LibraryDocumentId",
                        column: x => x.LibraryDocumentId,
                        principalTable: "LibraryDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdventureHandouts_LibraryDocumentId",
                table: "AdventureHandouts",
                column: "LibraryDocumentId");

            // AI use before feature 38 recorded it: a section revision is only ever written by a
            // Loremaster draft or Put in section, and an Edit or Put draft revision by the
            // Loremaster's words going into the design draft. Revisions are trimmed, so this can
            // miss old use (the GM can still tick the box); it never marks use that didn't happen.
            migrationBuilder.Sql("""
                UPDATE a SET a.[AiWroteText] = 1
                FROM [Adventures] a
                WHERE EXISTS (
                    SELECT 1 FROM [AdventureSections] s
                    JOIN [AdventureSectionRevisions] r ON r.[SectionId] = s.[Id]
                    WHERE s.[AdventureId] = a.[Id])
                OR EXISTS (
                    SELECT 1 FROM [AdventureDraftRevisions] d
                    WHERE d.[AdventureId] = a.[Id] AND d.[Reason] IN (N'Edit', N'Put'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdventureHandouts");

            migrationBuilder.DropColumn(
                name: "PrintDetails_AiUse",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "AiDeclared",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "AiMadeArt",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "AiWroteText",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "CoverBrief",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "CoverImagesGenerated",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "CoverOptionIds",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Cover_BlobPath",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Cover_Brief",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Cover_ContentType",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Cover_Layout",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Cover_Source",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Cover_UpdatedAt",
                table: "Adventures");
        }
    }
}
