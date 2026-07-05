using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameThreadToStoryline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ArtifactType.Thread was renamed to ArtifactType.Storyline to match the product
            // vocabulary. Artifact.Type is persisted as a string (HasConversion<string>), so
            // existing rows must be updated to the new enum name.
            migrationBuilder.Sql("UPDATE [Artifacts] SET [Type] = 'Storyline' WHERE [Type] = 'Thread';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Artifacts] SET [Type] = 'Thread' WHERE [Type] = 'Storyline';");
        }
    }
}
