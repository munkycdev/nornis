using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrepDesignMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DraftMarkdown",
                table: "Adventures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DraftUpdatedAt",
                table: "Adventures",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TableBrief",
                table: "Adventures",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdventureDraftRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdventureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BodyMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureDraftRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdventureDraftRevisions_Adventures_AdventureId",
                        column: x => x.AdventureId,
                        principalTable: "Adventures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdventureSourceBooks",
                columns: table => new
                {
                    AdventureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LibraryDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureSourceBooks", x => new { x.AdventureId, x.LibraryDocumentId });
                    table.ForeignKey(
                        name: "FK_AdventureSourceBooks_Adventures_AdventureId",
                        column: x => x.AdventureId,
                        principalTable: "Adventures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdventureSourceBooks_LibraryDocuments_LibraryDocumentId",
                        column: x => x.LibraryDocumentId,
                        principalTable: "LibraryDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdventureDraftRevisions_AdventureId_CreatedAt",
                table: "AdventureDraftRevisions",
                columns: new[] { "AdventureId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdventureSourceBooks_LibraryDocumentId",
                table: "AdventureSourceBooks",
                column: "LibraryDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdventureDraftRevisions");

            migrationBuilder.DropTable(
                name: "AdventureSourceBooks");

            migrationBuilder.DropColumn(
                name: "DraftMarkdown",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "DraftUpdatedAt",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "TableBrief",
                table: "Adventures");
        }
    }
}
