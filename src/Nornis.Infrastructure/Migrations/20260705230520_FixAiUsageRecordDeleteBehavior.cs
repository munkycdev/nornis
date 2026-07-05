using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixAiUsageRecordDeleteBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiUsageRecords_Campaigns_CampaignId",
                table: "AiUsageRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_AiUsageRecords_Users_UserId",
                table: "AiUsageRecords");

            migrationBuilder.AddForeignKey(
                name: "FK_AiUsageRecords_Campaigns_CampaignId",
                table: "AiUsageRecords",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AiUsageRecords_Users_UserId",
                table: "AiUsageRecords",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiUsageRecords_Campaigns_CampaignId",
                table: "AiUsageRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_AiUsageRecords_Users_UserId",
                table: "AiUsageRecords");

            migrationBuilder.AddForeignKey(
                name: "FK_AiUsageRecords_Campaigns_CampaignId",
                table: "AiUsageRecords",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AiUsageRecords_Users_UserId",
                table: "AiUsageRecords",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");
        }
    }
}
