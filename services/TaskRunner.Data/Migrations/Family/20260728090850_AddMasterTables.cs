using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskRunner.Data.Migrations.Family
{
    /// <inheritdoc />
    public partial class AddMasterTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 安全删除：使用 IF EXISTS 防止表已不存在时迁移失败
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"MobileLogs\"");

            migrationBuilder.AddColumn<string>(
                name: "SharedSecret",
                table: "ServerAddressSettings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ApprenticeProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MasterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Foundation = table.Column<string>(type: "TEXT", nullable: true),
                    LearningStyle = table.Column<string>(type: "TEXT", nullable: true),
                    Strengths = table.Column<string>(type: "TEXT", nullable: true),
                    Weaknesses = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprenticeProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MasterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StageName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    PassProbability = table.Column<double>(type: "REAL", nullable: false),
                    WeakPointsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    Advice = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MasterConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MasterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Masters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MasterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MasterName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Goal = table.Column<string>(type: "TEXT", nullable: false),
                    Industry = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CurrentStage = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "入道"),
                    GraduatedStagesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "active"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Masters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StageSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MasterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StageName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VaultFocusStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MasterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VaultId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "focused"),
                    StageName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultFocusStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VaultFreeStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VaultId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "discovered"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultFreeStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprenticeProfiles_MasterId",
                table: "ApprenticeProfiles",
                column: "MasterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamCheckpoints_MasterId",
                table: "ExamCheckpoints",
                column: "MasterId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamCheckpoints_MasterId_StageName",
                table: "ExamCheckpoints",
                columns: new[] { "MasterId", "StageName" });

            migrationBuilder.CreateIndex(
                name: "IX_MasterConversations_MasterId",
                table: "MasterConversations",
                column: "MasterId");

            migrationBuilder.CreateIndex(
                name: "IX_MasterConversations_MasterId_CreatedAt",
                table: "MasterConversations",
                columns: new[] { "MasterId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Masters_MasterId",
                table: "Masters",
                column: "MasterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageSummaries_MasterId_StageName",
                table: "StageSummaries",
                columns: new[] { "MasterId", "StageName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaultFocusStates_MasterId",
                table: "VaultFocusStates",
                column: "MasterId");

            migrationBuilder.CreateIndex(
                name: "IX_VaultFocusStates_MasterId_VaultId",
                table: "VaultFocusStates",
                columns: new[] { "MasterId", "VaultId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaultFreeStates_VaultId",
                table: "VaultFreeStates",
                column: "VaultId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprenticeProfiles");

            migrationBuilder.DropTable(
                name: "ExamCheckpoints");

            migrationBuilder.DropTable(
                name: "MasterConversations");

            migrationBuilder.DropTable(
                name: "Masters");

            migrationBuilder.DropTable(
                name: "StageSummaries");

            migrationBuilder.DropTable(
                name: "VaultFocusStates");

            migrationBuilder.DropTable(
                name: "VaultFreeStates");

            migrationBuilder.DropColumn(
                name: "SharedSecret",
                table: "ServerAddressSettings");

            migrationBuilder.CreateTable(
                name: "MobileLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Context = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExtraJson = table.Column<string>(type: "TEXT", nullable: true),
                    Level = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "info"),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    ServerTimestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MobileLogs_DeviceId",
                table: "MobileLogs",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileLogs_Timestamp",
                table: "MobileLogs",
                column: "Timestamp");
        }
    }
}
