using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicWorldAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PublicAccessEnabled",
                table: "Worlds",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PublicSlug",
                table: "Worlds",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Worlds_PublicSlug",
                table: "Worlds",
                column: "PublicSlug",
                unique: true,
                filter: "[PublicSlug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Worlds_PublicSlug",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "PublicAccessEnabled",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "PublicSlug",
                table: "Worlds");
        }
    }
}
