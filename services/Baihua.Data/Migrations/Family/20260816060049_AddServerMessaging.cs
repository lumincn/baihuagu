using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baihua.Data.Migrations.Family
{
    /// <inheritdoc />
    public partial class AddServerMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeerServerId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PeerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerPeers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "manual"),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerPeers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerMessages_PeerId",
                table: "ServerMessages",
                column: "PeerId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerMessages_PeerServerId_SentAtUtc",
                table: "ServerMessages",
                columns: new[] { "PeerServerId", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ServerPeers_ServerId",
                table: "ServerPeers",
                column: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerMessages");

            migrationBuilder.DropTable(
                name: "ServerPeers");
        }
    }
}
