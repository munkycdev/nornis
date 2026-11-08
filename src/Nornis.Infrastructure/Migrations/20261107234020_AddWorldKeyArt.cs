using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldKeyArt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyArtBrief",
                table: "Worlds",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KeyArtImagesGenerated",
                table: "Worlds",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "KeyArtImagesMonth",
                table: "Worlds",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyArtOptionIds",
                table: "Worlds",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "KeyArt_Anchor",
                table: "Worlds",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyArt_BlobPath",
                table: "Worlds",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyArt_Brief",
                table: "Worlds",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyArt_ContentType",
                table: "Worlds",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyArt_Source",
                table: "Worlds",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KeyArt_UpdatedAt",
                table: "Worlds",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeyArtBrief",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "KeyArtImagesGenerated",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "KeyArtImagesMonth",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "KeyArtOptionIds",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "KeyArt_Anchor",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "KeyArt_BlobPath",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "KeyArt_Brief",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "KeyArt_ContentType",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "KeyArt_Source",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "KeyArt_UpdatedAt",
                table: "Worlds");
        }
    }
}
