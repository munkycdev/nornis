using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeEntityCreator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Artifacts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "ArtifactRelationships",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "ArtifactFacts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_CreatedByUserId",
                table: "Artifacts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactRelationships_CreatedByUserId",
                table: "ArtifactRelationships",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactFacts_CreatedByUserId",
                table: "ArtifactFacts",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ArtifactFacts_Users_CreatedByUserId",
                table: "ArtifactFacts",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ArtifactRelationships_Users_CreatedByUserId",
                table: "ArtifactRelationships",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Artifacts_Users_CreatedByUserId",
                table: "Artifacts",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill: each entity's owner is the author of the source that created it —
            // derived via the earliest SourceReference (updates append more references
            // later, so MIN CreatedAt identifies creation; Id tie-breaks for determinism).
            // Rows with no reference (pre-attribution PartOf links, orphaned legacy rows)
            // stay NULL, which the visibility policy treats as GM-only for Private scope.
            migrationBuilder.Sql("""
                UPDATE a SET CreatedByUserId = x.CreatedByUserId
                FROM Artifacts a
                CROSS APPLY (
                    SELECT TOP 1 s.CreatedByUserId
                    FROM SourceReferences sr
                    JOIN Sources s ON s.Id = sr.SourceId
                    WHERE sr.TargetType = N'Artifact' AND sr.TargetId = a.Id
                    ORDER BY sr.CreatedAt ASC, sr.Id ASC
                ) x;
                """);

            migrationBuilder.Sql("""
                UPDATE f SET CreatedByUserId = x.CreatedByUserId
                FROM ArtifactFacts f
                CROSS APPLY (
                    SELECT TOP 1 s.CreatedByUserId
                    FROM SourceReferences sr
                    JOIN Sources s ON s.Id = sr.SourceId
                    WHERE sr.TargetType = N'ArtifactFact' AND sr.TargetId = f.Id
                    ORDER BY sr.CreatedAt ASC, sr.Id ASC
                ) x;
                """);

            migrationBuilder.Sql("""
                UPDATE r SET CreatedByUserId = x.CreatedByUserId
                FROM ArtifactRelationships r
                CROSS APPLY (
                    SELECT TOP 1 s.CreatedByUserId
                    FROM SourceReferences sr
                    JOIN Sources s ON s.Id = sr.SourceId
                    WHERE sr.TargetType = N'ArtifactRelationship' AND sr.TargetId = r.Id
                    ORDER BY sr.CreatedAt ASC, sr.Id ASC
                ) x;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArtifactFacts_Users_CreatedByUserId",
                table: "ArtifactFacts");

            migrationBuilder.DropForeignKey(
                name: "FK_ArtifactRelationships_Users_CreatedByUserId",
                table: "ArtifactRelationships");

            migrationBuilder.DropForeignKey(
                name: "FK_Artifacts_Users_CreatedByUserId",
                table: "Artifacts");

            migrationBuilder.DropIndex(
                name: "IX_Artifacts_CreatedByUserId",
                table: "Artifacts");

            migrationBuilder.DropIndex(
                name: "IX_ArtifactRelationships_CreatedByUserId",
                table: "ArtifactRelationships");

            migrationBuilder.DropIndex(
                name: "IX_ArtifactFacts_CreatedByUserId",
                table: "ArtifactFacts");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Artifacts");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ArtifactRelationships");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ArtifactFacts");
        }
    }
}
