using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261112175421_AddCheckIn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CheckInQuestionCount",
            table: "Worlds",
            type: "int",
            nullable: false,
            defaultValue: 5);

        migrationBuilder.CreateTable(
            name: "CheckIns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                LatestSessionSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LatestSessionAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CheckIns", x => x.Id);
                table.ForeignKey(
                    name: "FK_CheckIns_Worlds_WorldId",
                    column: x => x.WorldId,
                    principalTable: "Worlds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EditorialRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                CheckInQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EditorialRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_EditorialRules_Worlds_WorldId",
                    column: x => x.WorldId,
                    principalTable: "Worlds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CheckInQuestions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CheckInId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Order = table.Column<int>(type: "int", nullable: false),
                Asked = table.Column<bool>(type: "bit", nullable: false),
                Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                SubjectArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SubjectLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Why = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                SuggestedRule = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                SuggestedRecordOnArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Answer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                AnsweredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                AnsweredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ActionBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RecordedOnArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CheckInQuestions", x => x.Id);
                table.ForeignKey(
                    name: "FK_CheckInQuestions_CheckIns_CheckInId",
                    column: x => x.CheckInId,
                    principalTable: "CheckIns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CheckInQuestions_CheckInId_Order",
            table: "CheckInQuestions",
            columns: new[] { "CheckInId", "Order" });

        migrationBuilder.CreateIndex(
            name: "IX_CheckIns_WorldId_CreatedAt",
            table: "CheckIns",
            columns: new[] { "WorldId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_EditorialRules_WorldId",
            table: "EditorialRules",
            column: "WorldId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CheckInQuestions");

        migrationBuilder.DropTable(
            name: "EditorialRules");

        migrationBuilder.DropTable(
            name: "CheckIns");

        migrationBuilder.DropColumn(
            name: "CheckInQuestionCount",
            table: "Worlds");
    }
}
