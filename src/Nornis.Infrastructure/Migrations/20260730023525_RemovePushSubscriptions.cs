using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nornis.Infrastructure.Migrations;

/// <summary>
/// Removes the WebPush feature's storage. Browser push was inert in production — the VAPID
/// keys were never set on either live app — so nothing user-visible is lost.
///
/// UNLIKE every other migration in this repo, apply this one AFTER the new images are live,
/// not before. The usual pre-deploy order exists so additive changes are in place when the
/// new code arrives; a destructive drop inverts that — the OLD image still serves
/// /api/notifications until the deploy lands, and would 500 against a dropped table.
/// </summary>
public partial class RemovePushSubscriptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PushSubscriptions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PushSubscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Auth = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Endpoint = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                LastSucceededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                P256dh = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                table.ForeignKey(
                    name: "FK_PushSubscriptions_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PushSubscriptions_Endpoint",
            table: "PushSubscriptions",
            column: "Endpoint",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PushSubscriptions_UserId",
            table: "PushSubscriptions",
            column: "UserId");
    }
}
