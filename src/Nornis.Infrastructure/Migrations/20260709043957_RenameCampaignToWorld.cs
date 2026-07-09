using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <summary>
    /// The root entity Campaign was renamed to World so a single body of knowledge can
    /// host multiple campaigns. This migration is hand-written: EF scaffolds a rename as
    /// drop/create, which would destroy data. Everything here is a rename — tables,
    /// columns, indexes, and constraint names — so existing rows are preserved.
    /// NOTE: not an additive migration; old app revisions fail against the renamed
    /// schema during the deploy window. Acceptable for the current single-user install.
    /// </summary>
    public partial class RenameCampaignToWorld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop every FK whose name or target changes; re-added with EF's new
            // conventional names after the renames so future scaffolds line up.
            migrationBuilder.DropForeignKey(
                name: "FK_AiUsageRecords_Campaigns_CampaignId",
                table: "AiUsageRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_ArtifactRelationships_Campaigns_CampaignId",
                table: "ArtifactRelationships");

            migrationBuilder.DropForeignKey(
                name: "FK_Artifacts_Campaigns_CampaignId",
                table: "Artifacts");

            migrationBuilder.DropForeignKey(
                name: "FK_HealthAssessments_Campaigns_CampaignId",
                table: "HealthAssessments");

            migrationBuilder.DropForeignKey(
                name: "FK_ReviewBatches_Campaigns_CampaignId",
                table: "ReviewBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_Sources_Campaigns_CampaignId",
                table: "Sources");

            migrationBuilder.DropForeignKey(
                name: "FK_CampaignMembers_Campaigns_CampaignId",
                table: "CampaignMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_CampaignMembers_Users_UserId",
                table: "CampaignMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_Users_CreatedByUserId",
                table: "Campaigns");

            // Root tables.
            migrationBuilder.RenameTable(
                name: "Campaigns",
                newName: "Worlds");

            migrationBuilder.RenameTable(
                name: "CampaignMembers",
                newName: "WorldMembers");

            migrationBuilder.Sql("EXEC sp_rename N'[PK_Campaigns]', N'PK_Worlds', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'[PK_CampaignMembers]', N'PK_WorldMembers', N'OBJECT';");

            migrationBuilder.RenameColumn(
                name: "CampaignId",
                table: "WorldMembers",
                newName: "WorldId");

            migrationBuilder.RenameIndex(
                name: "IX_CampaignMembers_CampaignId_UserId",
                table: "WorldMembers",
                newName: "IX_WorldMembers_WorldId_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_CampaignMembers_UserId",
                table: "WorldMembers",
                newName: "IX_WorldMembers_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Campaigns_CreatedByUserId",
                table: "Worlds",
                newName: "IX_Worlds_CreatedByUserId");

            // Child FK columns and their indexes.
            migrationBuilder.RenameColumn(
                name: "CampaignId",
                table: "Sources",
                newName: "WorldId");

            migrationBuilder.RenameIndex(
                name: "IX_Sources_CampaignId_ProcessingStatus",
                table: "Sources",
                newName: "IX_Sources_WorldId_ProcessingStatus");

            migrationBuilder.RenameColumn(
                name: "CampaignId",
                table: "ReviewBatches",
                newName: "WorldId");

            migrationBuilder.RenameIndex(
                name: "IX_ReviewBatches_CampaignId",
                table: "ReviewBatches",
                newName: "IX_ReviewBatches_WorldId");

            migrationBuilder.RenameColumn(
                name: "CampaignId",
                table: "HealthAssessments",
                newName: "WorldId");

            migrationBuilder.RenameIndex(
                name: "IX_HealthAssessments_CampaignId_CreatedAt",
                table: "HealthAssessments",
                newName: "IX_HealthAssessments_WorldId_CreatedAt");

            migrationBuilder.RenameColumn(
                name: "CampaignId",
                table: "Artifacts",
                newName: "WorldId");

            migrationBuilder.RenameIndex(
                name: "IX_Artifacts_CampaignId",
                table: "Artifacts",
                newName: "IX_Artifacts_WorldId");

            migrationBuilder.RenameColumn(
                name: "CampaignId",
                table: "ArtifactRelationships",
                newName: "WorldId");

            migrationBuilder.RenameIndex(
                name: "IX_ArtifactRelationships_CampaignId",
                table: "ArtifactRelationships",
                newName: "IX_ArtifactRelationships_WorldId");

            migrationBuilder.RenameColumn(
                name: "CampaignId",
                table: "AiUsageRecords",
                newName: "WorldId");

            migrationBuilder.RenameIndex(
                name: "IX_AiUsageRecords_CampaignId",
                table: "AiUsageRecords",
                newName: "IX_AiUsageRecords_WorldId");

            // Re-add FKs under EF's conventional names for the renamed schema.
            migrationBuilder.AddForeignKey(
                name: "FK_Worlds_Users_CreatedByUserId",
                table: "Worlds",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorldMembers_Worlds_WorldId",
                table: "WorldMembers",
                column: "WorldId",
                principalTable: "Worlds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WorldMembers_Users_UserId",
                table: "WorldMembers",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AiUsageRecords_Worlds_WorldId",
                table: "AiUsageRecords",
                column: "WorldId",
                principalTable: "Worlds",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ArtifactRelationships_Worlds_WorldId",
                table: "ArtifactRelationships",
                column: "WorldId",
                principalTable: "Worlds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Artifacts_Worlds_WorldId",
                table: "Artifacts",
                column: "WorldId",
                principalTable: "Worlds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HealthAssessments_Worlds_WorldId",
                table: "HealthAssessments",
                column: "WorldId",
                principalTable: "Worlds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReviewBatches_Worlds_WorldId",
                table: "ReviewBatches",
                column: "WorldId",
                principalTable: "Worlds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sources_Worlds_WorldId",
                table: "Sources",
                column: "WorldId",
                principalTable: "Worlds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiUsageRecords_Worlds_WorldId",
                table: "AiUsageRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_ArtifactRelationships_Worlds_WorldId",
                table: "ArtifactRelationships");

            migrationBuilder.DropForeignKey(
                name: "FK_Artifacts_Worlds_WorldId",
                table: "Artifacts");

            migrationBuilder.DropForeignKey(
                name: "FK_HealthAssessments_Worlds_WorldId",
                table: "HealthAssessments");

            migrationBuilder.DropForeignKey(
                name: "FK_ReviewBatches_Worlds_WorldId",
                table: "ReviewBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_Sources_Worlds_WorldId",
                table: "Sources");

            migrationBuilder.DropForeignKey(
                name: "FK_WorldMembers_Worlds_WorldId",
                table: "WorldMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_WorldMembers_Users_UserId",
                table: "WorldMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_Worlds_Users_CreatedByUserId",
                table: "Worlds");

            migrationBuilder.RenameTable(
                name: "Worlds",
                newName: "Campaigns");

            migrationBuilder.RenameTable(
                name: "WorldMembers",
                newName: "CampaignMembers");

            migrationBuilder.Sql("EXEC sp_rename N'[PK_Worlds]', N'PK_Campaigns', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'[PK_WorldMembers]', N'PK_CampaignMembers', N'OBJECT';");

            migrationBuilder.RenameColumn(
                name: "WorldId",
                table: "CampaignMembers",
                newName: "CampaignId");

            migrationBuilder.RenameIndex(
                name: "IX_WorldMembers_WorldId_UserId",
                table: "CampaignMembers",
                newName: "IX_CampaignMembers_CampaignId_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_WorldMembers_UserId",
                table: "CampaignMembers",
                newName: "IX_CampaignMembers_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Worlds_CreatedByUserId",
                table: "Campaigns",
                newName: "IX_Campaigns_CreatedByUserId");

            migrationBuilder.RenameColumn(
                name: "WorldId",
                table: "Sources",
                newName: "CampaignId");

            migrationBuilder.RenameIndex(
                name: "IX_Sources_WorldId_ProcessingStatus",
                table: "Sources",
                newName: "IX_Sources_CampaignId_ProcessingStatus");

            migrationBuilder.RenameColumn(
                name: "WorldId",
                table: "ReviewBatches",
                newName: "CampaignId");

            migrationBuilder.RenameIndex(
                name: "IX_ReviewBatches_WorldId",
                table: "ReviewBatches",
                newName: "IX_ReviewBatches_CampaignId");

            migrationBuilder.RenameColumn(
                name: "WorldId",
                table: "HealthAssessments",
                newName: "CampaignId");

            migrationBuilder.RenameIndex(
                name: "IX_HealthAssessments_WorldId_CreatedAt",
                table: "HealthAssessments",
                newName: "IX_HealthAssessments_CampaignId_CreatedAt");

            migrationBuilder.RenameColumn(
                name: "WorldId",
                table: "Artifacts",
                newName: "CampaignId");

            migrationBuilder.RenameIndex(
                name: "IX_Artifacts_WorldId",
                table: "Artifacts",
                newName: "IX_Artifacts_CampaignId");

            migrationBuilder.RenameColumn(
                name: "WorldId",
                table: "ArtifactRelationships",
                newName: "CampaignId");

            migrationBuilder.RenameIndex(
                name: "IX_ArtifactRelationships_WorldId",
                table: "ArtifactRelationships",
                newName: "IX_ArtifactRelationships_CampaignId");

            migrationBuilder.RenameColumn(
                name: "WorldId",
                table: "AiUsageRecords",
                newName: "CampaignId");

            migrationBuilder.RenameIndex(
                name: "IX_AiUsageRecords_WorldId",
                table: "AiUsageRecords",
                newName: "IX_AiUsageRecords_CampaignId");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_Users_CreatedByUserId",
                table: "Campaigns",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CampaignMembers_Campaigns_CampaignId",
                table: "CampaignMembers",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CampaignMembers_Users_UserId",
                table: "CampaignMembers",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AiUsageRecords_Campaigns_CampaignId",
                table: "AiUsageRecords",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ArtifactRelationships_Campaigns_CampaignId",
                table: "ArtifactRelationships",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Artifacts_Campaigns_CampaignId",
                table: "Artifacts",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HealthAssessments_Campaigns_CampaignId",
                table: "HealthAssessments",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReviewBatches_Campaigns_CampaignId",
                table: "ReviewBatches",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sources_Campaigns_CampaignId",
                table: "Sources",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
