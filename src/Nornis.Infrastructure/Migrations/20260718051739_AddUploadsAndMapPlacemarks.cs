using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadsAndMapPlacemarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DerivedText",
                table: "Sources",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MapPlacemarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    X = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    Y = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapPlacemarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapPlacemarks_SourceAttachments_SourceAttachmentId",
                        column: x => x.SourceAttachmentId,
                        principalTable: "SourceAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapPlacemarks_ArtifactId",
                table: "MapPlacemarks",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_MapPlacemarks_SourceAttachmentId_ArtifactId",
                table: "MapPlacemarks",
                columns: new[] { "SourceAttachmentId", "ArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MapPlacemarks_WorldId",
                table: "MapPlacemarks",
                column: "WorldId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapPlacemarks");

            migrationBuilder.DropColumn(
                name: "DerivedText",
                table: "Sources");
        }
    }
}
