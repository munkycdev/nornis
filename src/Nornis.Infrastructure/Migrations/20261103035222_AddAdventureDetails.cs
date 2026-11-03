using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdventureDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrintDetails_Author",
                table: "LibraryDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrintDetails_Edition",
                table: "LibraryDocuments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrintDetails_GameSystem",
                table: "LibraryDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrintDetails_Levels",
                table: "LibraryDocuments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrintDetails_Publisher",
                table: "LibraryDocuments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrintDetails_Title",
                table: "LibraryDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrintDetails_Type",
                table: "LibraryDocuments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrintDetails_Year",
                table: "LibraryDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details_Edition",
                table: "Adventures",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details_GameSystem",
                table: "Adventures",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details_Levels",
                table: "Adventures",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Details_Year",
                table: "Adventures",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrintDetails_Author",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "PrintDetails_Edition",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "PrintDetails_GameSystem",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "PrintDetails_Levels",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "PrintDetails_Publisher",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "PrintDetails_Title",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "PrintDetails_Type",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "PrintDetails_Year",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "Details_Edition",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Details_GameSystem",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Details_Levels",
                table: "Adventures");

            migrationBuilder.DropColumn(
                name: "Details_Year",
                table: "Adventures");
        }
    }
}
