using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiKeyId",
                table: "AiUsageRecords",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FundedBy",
                table: "AiUsageRecords",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "KeyHolderUserId",
                table: "AiUsageRecords",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                table: "AiUsageRecords",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "AiUsageRecords",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserAiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProtectedKey = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    LastFour = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckFailed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAiKeys_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAiModelChoices",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Task = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiModelChoices", x => new { x.UserId, x.Task });
                    table.ForeignKey(
                        name: "FK_UserAiModelChoices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAiCompatibleModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserAiKeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Task = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SupportsImages = table.Column<bool>(type: "bit", nullable: false),
                    InputPerMillionUsd = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    OutputPerMillionUsd = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiCompatibleModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAiCompatibleModels_UserAiKeys_UserAiKeyId",
                        column: x => x.UserAiKeyId,
                        principalTable: "UserAiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_KeyHolderUserId_FundedBy_CreatedAt",
                table: "AiUsageRecords",
                columns: new[] { "KeyHolderUserId", "FundedBy", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAiCompatibleModels_UserAiKeyId_Task",
                table: "UserAiCompatibleModels",
                columns: new[] { "UserAiKeyId", "Task" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAiKeys_UserId_Provider",
                table: "UserAiKeys",
                columns: new[] { "UserId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAiCompatibleModels");

            migrationBuilder.DropTable(
                name: "UserAiModelChoices");

            migrationBuilder.DropTable(
                name: "UserAiKeys");

            migrationBuilder.DropIndex(
                name: "IX_AiUsageRecords_KeyHolderUserId_FundedBy_CreatedAt",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "AiKeyId",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "FundedBy",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "KeyHolderUserId",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AiUsageRecords");
        }
    }
}
