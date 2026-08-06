using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldDigests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorldDigests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GmContentMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartyContentMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorldDigests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorldDigests_Worlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "Worlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorldDigests_WorldId",
                table: "WorldDigests",
                column: "WorldId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorldDigests");
        }
    }
}
