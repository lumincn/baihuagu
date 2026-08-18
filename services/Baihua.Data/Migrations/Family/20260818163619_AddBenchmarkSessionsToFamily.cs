using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baihua.Data.Migrations.Family
{
    /// <inheritdoc />
    public partial class AddBenchmarkSessionsToFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkSessions");
        }
    }
}
