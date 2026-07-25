using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingAndTutorialProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingPromptSeenAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TutorialDismissedAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TutorialProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorialProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TutorialProgress_Worlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "Worlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TutorialProgress_UserId_WorldId_StepKey",
                table: "TutorialProgress",
                columns: new[] { "UserId", "WorldId", "StepKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TutorialProgress_WorldId",
                table: "TutorialProgress",
                column: "WorldId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TutorialProgress");

            migrationBuilder.DropColumn(
                name: "OnboardingPromptSeenAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TutorialDismissedAt",
                table: "Users");
        }
    }
}
