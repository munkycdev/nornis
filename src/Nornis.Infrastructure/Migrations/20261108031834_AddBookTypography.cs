using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookTypography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Typography_Font",
                table: "Adventures",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Typography_LineSpacing",
                table: "Adventures",
                type: "decimal(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Typography_Size",
                table: "Adventures",
                type: "decimal(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Typography_Font",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Typography_LineSpacing",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Typography_Size",
                table: "Adventures");
        }
    }
}
