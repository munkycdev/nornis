using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBibliotaph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BibliotaphDocumentId",
                table: "LibraryDocuments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BibliotaphEntryId",
                table: "LibraryDocuments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverName",
                table: "LibraryDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "LibraryDocuments",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                // Every existing row is an upload. Edited from the scaffolded "", which would
                // leave rows no LibraryDocumentOrigin member can read.
                defaultValue: "Upload");

            migrationBuilder.AddColumn<string>(
                name: "RemoteTitle",
                table: "LibraryDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedAt",
                table: "LibraryDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BibliotaphConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BibliotaphConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BibliotaphConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BibliotaphWorldEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WorldName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BibliotaphWorldEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BibliotaphWorldEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryReadingRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LibraryDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ViewerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryReadingRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryReadingRecords_LibraryDocuments_LibraryDocumentId",
                        column: x => x.LibraryDocumentId,
                        principalTable: "LibraryDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryDocuments_WorldId_BibliotaphEntryId",
                table: "LibraryDocuments",
                columns: new[] { "WorldId", "BibliotaphEntryId" },
                unique: true,
                filter: "[BibliotaphEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BibliotaphConnections_UserId",
                table: "BibliotaphConnections",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BibliotaphWorldEvents_DeliveredAt_FailedAt_NextAttemptAt",
                table: "BibliotaphWorldEvents",
                columns: new[] { "DeliveredAt", "FailedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BibliotaphWorldEvents_UserId_OccurredAt",
                table: "BibliotaphWorldEvents",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReadingRecords_LibraryDocumentId_OpenedAt",
                table: "LibraryReadingRecords",
                columns: new[] { "LibraryDocumentId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryReadingRecords_OpenedAt",
                table: "LibraryReadingRecords",
                column: "OpenedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BibliotaphConnections");

            migrationBuilder.DropTable(
                name: "BibliotaphWorldEvents");

            migrationBuilder.DropTable(
                name: "LibraryReadingRecords");

            migrationBuilder.DropIndex(
                name: "IX_LibraryDocuments_WorldId_BibliotaphEntryId",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "BibliotaphDocumentId",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "BibliotaphEntryId",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "CoverName",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "RemoteTitle",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "LibraryDocuments");
        }
    }
}
