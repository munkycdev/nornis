using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PrintDetails_ResearchedOnWeb",
                table: "LibraryDocuments",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebResearchJson",
                table: "ConversationMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WebSearches",
                table: "AiUsageRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiResearchedOnWeb",
                table: "Adventures",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrintDetails_ResearchedOnWeb",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "WebResearchJson",
                table: "ConversationMessages");

            migrationBuilder.DropColumn(
                name: "WebSearches",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "AiResearchedOnWeb",
                table: "Adventures");
        }
    }
}
