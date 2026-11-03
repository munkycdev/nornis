using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSignIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignInLinkCodes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignInLinkCodes", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_SignInLinkCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSignIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSignIns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSignIns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignInLinkCodes_CodeHash",
                table: "SignInLinkCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSignIns_Subject",
                table: "UserSignIns",
                column: "Subject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSignIns_UserId",
                table: "UserSignIns",
                column: "UserId");

            // Every user keeps the subject they have as their one sign-in, then the column goes:
            // the map has one source of truth (feature 33).
            migrationBuilder.Sql(
                """
                INSERT INTO UserSignIns (Id, UserId, Subject, AddedAt)
                SELECT NEWID(), Id, Auth0SubjectId, CreatedAt FROM Users;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Users_Auth0SubjectId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Auth0SubjectId",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Auth0SubjectId",
                table: "Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Back to one subject per user: each keeps its oldest sign-in. Any others are lost.
            migrationBuilder.Sql(
                """
                UPDATE u SET Auth0SubjectId = s.Subject
                FROM Users u
                CROSS APPLY (SELECT TOP 1 Subject FROM UserSignIns WHERE UserId = u.Id ORDER BY AddedAt, Id) s;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Auth0SubjectId",
                table: "Users",
                column: "Auth0SubjectId",
                unique: true);

            migrationBuilder.DropTable(
                name: "SignInLinkCodes");

            migrationBuilder.DropTable(
                name: "UserSignIns");
        }
    }
}
