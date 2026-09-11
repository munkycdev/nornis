using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEntitySlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Sources",
                type: "nvarchar(65)",
                maxLength: 65,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "LibraryDocuments",
                type: "nvarchar(65)",
                maxLength: 65,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Characters",
                type: "nvarchar(65)",
                maxLength: 65,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Campaigns",
                type: "nvarchar(65)",
                maxLength: 65,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Artifacts",
                type: "nvarchar(65)",
                maxLength: 65,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sources_WorldId_Slug",
                table: "Sources",
                columns: new[] { "WorldId", "Slug" },
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryDocuments_WorldId_Slug",
                table: "LibraryDocuments",
                columns: new[] { "WorldId", "Slug" },
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_WorldId_Slug",
                table: "Characters",
                columns: new[] { "WorldId", "Slug" },
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_WorldId_Slug",
                table: "Campaigns",
                columns: new[] { "WorldId", "Slug" },
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_WorldId_Slug",
                table: "Artifacts",
                columns: new[] { "WorldId", "Slug" },
                unique: true,
                filter: "[Slug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sources_WorldId_Slug",
                table: "Sources");

            migrationBuilder.DropIndex(
                name: "IX_LibraryDocuments_WorldId_Slug",
                table: "LibraryDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Characters_WorldId_Slug",
                table: "Characters");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_WorldId_Slug",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Artifacts_WorldId_Slug",
                table: "Artifacts");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Artifacts");
        }
    }
}
