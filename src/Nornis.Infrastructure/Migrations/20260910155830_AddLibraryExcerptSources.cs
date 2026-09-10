using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryExcerptSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LibraryDocumentId",
                table: "Sources",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LibraryPageFrom",
                table: "Sources",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LibraryPageTo",
                table: "Sources",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sources_LibraryDocumentId",
                table: "Sources",
                column: "LibraryDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sources_LibraryDocuments_LibraryDocumentId",
                table: "Sources",
                column: "LibraryDocumentId",
                principalTable: "LibraryDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sources_LibraryDocuments_LibraryDocumentId",
                table: "Sources");

            migrationBuilder.DropIndex(
                name: "IX_Sources_LibraryDocumentId",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "LibraryDocumentId",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "LibraryPageFrom",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "LibraryPageTo",
                table: "Sources");
        }
    }
}
