using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskRunner.Data.Migrations.AI
{
    /// <inheritdoc />
    public partial class AIInitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiProviderSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AnthropicBaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EncryptedApiKey = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsMain = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    ModelsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiUsageMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CalledAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    LatencyMs = table.Column<long>(type: "INTEGER", nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    TotalTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    TokensPerSecond = table.Column<double>(type: "REAL", nullable: true),
                    IsSuccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TestedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ResultsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    AvgTokensPerSecond = table.Column<double>(type: "REAL", nullable: false),
                    AvgLatencyMs = table.Column<double>(type: "REAL", nullable: false),
                    AvgQualityScore = table.Column<double>(type: "REAL", nullable: false),
                    CompletionRate = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkSessions", x => x.Id);
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
                name: "EmbeddingConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Dimensions = table.Column<int>(type: "INTEGER", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmbeddingConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoteEmbeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VaultId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    NotePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    VectorJson = table.Column<string>(type: "TEXT", nullable: false),
                    Dimensions = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteEmbeddings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderSettings_IsMain",
                table: "AiProviderSettings",
                column: "IsMain");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderSettings_ProviderId",
                table: "AiProviderSettings",
                column: "ProviderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageMetrics_CalledAt",
                table: "AiUsageMetrics",
                column: "CalledAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageMetrics_ModelId",
                table: "AiUsageMetrics",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageMetrics_Operation",
                table: "AiUsageMetrics",
                column: "Operation");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageMetrics_ProviderId",
                table: "AiUsageMetrics",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkSessions_Category",
                table: "BenchmarkSessions",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkSessions_SessionId",
                table: "BenchmarkSessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkSessions_TestedAt",
                table: "BenchmarkSessions",
                column: "TestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMemoryEntries_SessionId_Round",
                table: "ChatMemoryEntries",
                columns: new[] { "SessionId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingConfigs_ProviderId",
                table: "EmbeddingConfigs",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteEmbeddings_VaultId_NotePath",
                table: "NoteEmbeddings",
                columns: new[] { "VaultId", "NotePath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiProviderSettings");

            migrationBuilder.DropTable(
                name: "AiUsageMetrics");

            migrationBuilder.DropTable(
                name: "BenchmarkSessions");

            migrationBuilder.DropTable(
                name: "ChatMemoryEntries");

            migrationBuilder.DropTable(
                name: "EmbeddingConfigs");

            migrationBuilder.DropTable(
                name: "NoteEmbeddings");
        }
    }
}
