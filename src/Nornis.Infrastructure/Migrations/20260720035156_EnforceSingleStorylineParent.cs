using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleStorylineParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "A storyline sits under one parent" was only ever a convention, so worlds can
            // hold storylines with several PartOf rows. Collapse each child to a single link
            // before the unique index below can be created.
            //
            // Which link survives: a hand-curated one wins over an AI-applied one (only the
            // latter carries a SourceReference), then the oldest, then by Id so the choice is
            // deterministic. That matches how the product treats the hierarchy — the GM curates
            // the tree and extraction only ever proposes into it.
            //
            // Surplus links are dropped, not archived. A GM can reassign any parent from the
            // artifact page afterwards.
            migrationBuilder.Sql("""
                SELECT Id
                INTO #SurplusPartOf
                FROM (
                    SELECT ar.Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY ar.ArtifactAId
                               ORDER BY
                                   CASE WHEN EXISTS (
                                       SELECT 1 FROM SourceReferences sr
                                       WHERE sr.TargetId = ar.Id
                                         AND sr.TargetType = 'ArtifactRelationship'
                                   ) THEN 1 ELSE 0 END,
                                   ar.CreatedAt,
                                   ar.Id
                           ) AS Rn
                    FROM ArtifactRelationships ar
                    WHERE ar.Type = 'PartOf'
                ) Ranked
                WHERE Ranked.Rn > 1;

                DELETE FROM SourceReferences
                WHERE TargetType = 'ArtifactRelationship'
                  AND TargetId IN (SELECT Id FROM #SurplusPartOf);

                DELETE FROM ArtifactRelationships
                WHERE Id IN (SELECT Id FROM #SurplusPartOf);

                DROP TABLE #SurplusPartOf;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactRelationships_SinglePartOfParent",
                table: "ArtifactRelationships",
                column: "ArtifactAId",
                unique: true,
                filter: "[Type] = 'PartOf'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArtifactRelationships_SinglePartOfParent",
                table: "ArtifactRelationships");
        }
    }
}
