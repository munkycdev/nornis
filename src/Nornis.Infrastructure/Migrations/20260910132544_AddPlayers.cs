using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations
{
    /// <summary>
    /// Feature 25: characters belong to players, not memberships. Hand-ordered rather than the
    /// scaffolded rename, because the column's meaning changes: every existing character's
    /// member becomes that member's newly created player, and the old column goes only once
    /// nothing is left pointing through it.
    ///
    /// Not additive. A running old revision reads Characters.WorldMemberId and errors between
    /// this and the rollout — the window RenameCampaignToWorld accepted, for the same
    /// single-user reason. After applying, both of these must be zero:
    /// <code>
    /// SELECT COUNT(*) FROM Characters WHERE PlayerId IS NULL;
    /// SELECT COUNT(*) FROM WorldMembers m LEFT JOIN Players p ON p.WorldMemberId = m.Id WHERE p.Id IS NULL;
    /// </code>
    /// (The first cannot be non-zero after step 3 succeeds; it is listed so the check is the
    /// same before and after.)
    /// </summary>
    public partial class AddPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The table, and the new column with nothing in it yet.
            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_WorldMembers_WorldMemberId",
                        column: x => x.WorldMemberId,
                        principalTable: "WorldMembers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Players_Worlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "Worlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_WorldId",
                table: "Players",
                column: "WorldId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_WorldId_WorldMemberId",
                table: "Players",
                columns: new[] { "WorldId", "WorldMemberId" },
                unique: true,
                filter: "[WorldMemberId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Players_WorldMemberId",
                table: "Players",
                column: "WorldMemberId",
                unique: true,
                filter: "[WorldMemberId] IS NOT NULL");

            migrationBuilder.AddColumn<Guid>(
                name: "PlayerId",
                table: "Characters",
                type: "uniqueidentifier",
                nullable: true);

            // 2. One linked player per existing member, then every character to its member's
            //    player. The name expression mirrors MemberDisplayName.For — legitimately: no
            //    compiler spans a migration and a service, and the fallback is the public-safe
            //    "User xxxxxxxx", never the username.
            migrationBuilder.Sql("""
                INSERT INTO Players (Id, WorldId, WorldMemberId, Name, CreatedAt, UpdatedAt)
                SELECT NEWID(), m.WorldId, m.Id,
                       COALESCE(NULLIF(LTRIM(RTRIM(m.DisplayName)), ''), CONCAT('User ', LEFT(CONVERT(varchar(36), m.UserId), 8))),
                       SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                FROM WorldMembers m;

                UPDATE c SET c.PlayerId = p.Id
                FROM Characters c
                JOIN Players p ON p.WorldMemberId = c.WorldMemberId;
                """);

            // 3. The new column becomes the one that matters.
            migrationBuilder.AlterColumn<Guid>(
                name: "PlayerId",
                table: "Characters",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_PlayerId",
                table: "Characters",
                column: "PlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Characters_Players_PlayerId",
                table: "Characters",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 4. The old one goes.
            migrationBuilder.DropForeignKey(
                name: "FK_Characters_WorldMembers_WorldMemberId",
                table: "Characters");

            migrationBuilder.DropIndex(
                name: "IX_Characters_WorldMemberId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "WorldMemberId",
                table: "Characters");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Characters of players who are not on Nornis have no member to return to and are
            // lost on the way down; that is the cost of the shape this migration leaves behind.
            migrationBuilder.AddColumn<Guid>(
                name: "WorldMemberId",
                table: "Characters",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE c SET c.WorldMemberId = p.WorldMemberId
                FROM Characters c
                JOIN Players p ON p.Id = c.PlayerId;

                DELETE FROM Characters WHERE WorldMemberId IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "WorldMemberId",
                table: "Characters",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_WorldMemberId",
                table: "Characters",
                column: "WorldMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_Characters_WorldMembers_WorldMemberId",
                table: "Characters",
                column: "WorldMemberId",
                principalTable: "WorldMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_Characters_Players_PlayerId",
                table: "Characters");

            migrationBuilder.DropIndex(
                name: "IX_Characters_PlayerId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "Characters");

            migrationBuilder.DropTable(
                name: "Players");
        }
    }
}
