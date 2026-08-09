using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldCurrentCampaign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentCampaignId",
                table: "Worlds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Worlds_CurrentCampaignId",
                table: "Worlds",
                column: "CurrentCampaignId");

            migrationBuilder.AddForeignKey(
                name: "FK_Worlds_Campaigns_CurrentCampaignId",
                table: "Worlds",
                column: "CurrentCampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Adopt what the capture form was already inferring. Before this column, the form
            // preselected a world's campaign when exactly one was Active; shipping the column
            // empty would take that away from every existing world until its GM went and
            // declared the obvious. Worlds with several active campaigns are left null on
            // purpose — that is the case nothing could infer, and the reason the column exists.
            //
            // MIN(Id) is deterministic rather than arbitrary here: the WHERE clause has already
            // established there is exactly one row to choose from.
            migrationBuilder.Sql("""
                UPDATE Worlds
                SET CurrentCampaignId = (
                    SELECT MIN(c.Id) FROM Campaigns c
                    WHERE c.WorldId = Worlds.Id AND c.Status = 'Active')
                WHERE CurrentCampaignId IS NULL
                  AND (SELECT COUNT(*) FROM Campaigns c
                       WHERE c.WorldId = Worlds.Id AND c.Status = 'Active') = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Worlds_Campaigns_CurrentCampaignId",
                table: "Worlds");

            migrationBuilder.DropIndex(
                name: "IX_Worlds_CurrentCampaignId",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "CurrentCampaignId",
                table: "Worlds");
        }
    }
}
