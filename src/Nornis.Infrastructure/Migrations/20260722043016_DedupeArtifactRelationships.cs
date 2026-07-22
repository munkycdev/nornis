using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <summary>
    /// Removes duplicate artifact relationships — rows sharing (WorldId, ArtifactAId,
    /// ArtifactBId, Type) — keeping the oldest row of each group. Source references that
    /// cited a deleted duplicate are re-pointed at the surviving row first, so no
    /// provenance is lost. ProposalApplicator now reinforces an existing edge instead of
    /// inserting another row, so the duplicates stop accumulating after this runs.
    /// </summary>
    public partial class DedupeArtifactRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
WITH Ranked AS (
    SELECT Id,
           FIRST_VALUE(Id) OVER (
               PARTITION BY WorldId, ArtifactAId, ArtifactBId, [Type]
               ORDER BY CreatedAt, Id) AS KeeperId
    FROM ArtifactRelationships
)
UPDATE sr
SET sr.TargetId = r.KeeperId
FROM SourceReferences sr
INNER JOIN Ranked r ON sr.TargetId = r.Id
WHERE sr.TargetType = 'ArtifactRelationship'
  AND r.Id <> r.KeeperId;
");

            migrationBuilder.Sql(@"
WITH Ranked AS (
    SELECT Id,
           FIRST_VALUE(Id) OVER (
               PARTITION BY WorldId, ArtifactAId, ArtifactBId, [Type]
               ORDER BY CreatedAt, Id) AS KeeperId
    FROM ArtifactRelationships
)
DELETE FROM ArtifactRelationships
WHERE Id IN (SELECT Id FROM Ranked WHERE Id <> KeeperId);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data cleanup is not reversible: the deleted rows were redundant duplicates.
        }
    }
}
