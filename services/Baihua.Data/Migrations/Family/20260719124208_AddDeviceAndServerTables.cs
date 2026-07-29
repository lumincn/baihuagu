using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskRunner.Data.Migrations.Family
{
    /// <inheritdoc />
    public partial class AddDeviceAndServerTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthorizedDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "Authorized"),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LastSyncTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AuthorizedTime = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    SyncCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSyncTime = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizedDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMemoryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    UserSummary = table.Column<string>(type: "TEXT", nullable: false),
                    AssistantSummary = table.Column<string>(type: "TEXT", nullable: false),
                    UserContent = table.Column<string>(type: "TEXT", nullable: false),
                    AssistantContent = table.Column<string>(type: "TEXT", nullable: false),
                    VectorJson = table.Column<string>(type: "TEXT", nullable: true),
                    Dimensions = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMemoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceSyncLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    VaultId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    FileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "manifest"),
                    SyncTime = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSyncLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MobileLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "info"),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    Context = table.Column<string>(type: "TEXT", nullable: false),
                    ExtraJson = table.Column<string>(type: "TEXT", nullable: true),
                    ServerTimestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerAddressSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false, defaultValue: ""),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false, defaultValue: ""),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, defaultValue: ""),
                    ServerInstanceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerAddressSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedDevices_AccessToken",
                table: "AuthorizedDevices",
                column: "AccessToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedDevices_DeviceId",
                table: "AuthorizedDevices",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedDevices_Status",
                table: "AuthorizedDevices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMemoryEntries_SessionId_Round",
                table: "ChatMemoryEntries",
                columns: new[] { "SessionId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSyncLogs_DeviceId",
                table: "DeviceSyncLogs",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSyncLogs_SyncTime",
                table: "DeviceSyncLogs",
                column: "SyncTime");

            migrationBuilder.CreateIndex(
                name: "IX_MobileLogs_DeviceId",
                table: "MobileLogs",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileLogs_Timestamp",
                table: "MobileLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorizedDevices");

            migrationBuilder.DropTable(
                name: "ChatMemoryEntries");

            migrationBuilder.DropTable(
                name: "DeviceSyncLogs");

            migrationBuilder.DropTable(
                name: "MobileLogs");

            migrationBuilder.DropTable(
                name: "ServerAddressSettings");
        }
    }
}
