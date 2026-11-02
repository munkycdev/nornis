using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PrepMonthlyBudgetUsd",
                table: "Worlds",
                type: "decimal(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdventureId",
                table: "Sources",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdventureId",
                table: "LibraryDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Adventures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: true),
                    Pitch = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LastPrintedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adventures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Adventures_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Adventures_LibraryDocuments_LastPrintedDocumentId",
                        column: x => x.LastPrintedDocumentId,
                        principalTable: "LibraryDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Adventures_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Adventures_Worlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "Worlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdventureSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdventureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdventureSections_Adventures_AdventureId",
                        column: x => x.AdventureId,
                        principalTable: "Adventures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdventureSectionRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BodyMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureSectionRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdventureSectionRevisions_AdventureSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "AdventureSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sources_AdventureId",
                table: "Sources",
                column: "AdventureId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryDocuments_AdventureId",
                table: "LibraryDocuments",
                column: "AdventureId");

            migrationBuilder.CreateIndex(
                name: "IX_Adventures_CampaignId",
                table: "Adventures",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_Adventures_CreatedByUserId",
                table: "Adventures",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Adventures_LastPrintedDocumentId",
                table: "Adventures",
                column: "LastPrintedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Adventures_WorldId",
                table: "Adventures",
                column: "WorldId");

            migrationBuilder.CreateIndex(
                name: "IX_Adventures_WorldId_Slug",
                table: "Adventures",
                columns: new[] { "WorldId", "Slug" },
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdventureSectionRevisions_SectionId_CreatedAt",
                table: "AdventureSectionRevisions",
                columns: new[] { "SectionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdventureSections_AdventureId_SortOrder",
                table: "AdventureSections",
                columns: new[] { "AdventureId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryDocuments_Adventures_AdventureId",
                table: "LibraryDocuments",
                column: "AdventureId",
                principalTable: "Adventures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sources_Adventures_AdventureId",
                table: "Sources",
                column: "AdventureId",
                principalTable: "Adventures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryDocuments_Adventures_AdventureId",
                table: "LibraryDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_Sources_Adventures_AdventureId",
                table: "Sources");

            migrationBuilder.DropTable(
                name: "AdventureSectionRevisions");

            migrationBuilder.DropTable(
                name: "AdventureSections");

            migrationBuilder.DropTable(
                name: "Adventures");

            migrationBuilder.DropIndex(
                name: "IX_Sources_AdventureId",
                table: "Sources");

            migrationBuilder.DropIndex(
                name: "IX_LibraryDocuments_AdventureId",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "PrepMonthlyBudgetUsd",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "AdventureId",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "AdventureId",
                table: "LibraryDocuments");
        }
    }
}
