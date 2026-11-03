using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryDocumentAdventureLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedAdventureId",
                table: "LibraryDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryDocuments_LinkedAdventureId",
                table: "LibraryDocuments",
                column: "LinkedAdventureId");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryDocuments_Adventures_LinkedAdventureId",
                table: "LibraryDocuments",
                column: "LinkedAdventureId",
                principalTable: "Adventures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryDocuments_Adventures_LinkedAdventureId",
                table: "LibraryDocuments");

            migrationBuilder.DropIndex(
                name: "IX_LibraryDocuments_LinkedAdventureId",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "LinkedAdventureId",
                table: "LibraryDocuments");
        }
    }
}
