using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterArtifactLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ArtifactId",
                table: "Characters",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_ArtifactId",
                table: "Characters",
                column: "ArtifactId");

            migrationBuilder.AddForeignKey(
                name: "FK_Characters_Artifacts_ArtifactId",
                table: "Characters",
                column: "ArtifactId",
                principalTable: "Artifacts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Characters_Artifacts_ArtifactId",
                table: "Characters");

            migrationBuilder.DropIndex(
                name: "IX_Characters_ArtifactId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "ArtifactId",
                table: "Characters");
        }
    }
}
