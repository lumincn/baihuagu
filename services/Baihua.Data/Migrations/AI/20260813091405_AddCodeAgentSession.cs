using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baihua.Data.Migrations.AI
{
    /// <inheritdoc />
    public partial class AddCodeAgentSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeAgentSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ToolMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "All"),
                    IsPipeline = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlanPro = table.Column<bool>(type: "INTEGER", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: true),
                    Research = table.Column<string>(type: "TEXT", nullable: true),
                    Code = table.Column<string>(type: "TEXT", nullable: true),
                    Review = table.Column<string>(type: "TEXT", nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeAgentSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeAgentSessions_CreatedAt",
                table: "CodeAgentSessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CodeAgentSessions_IsPipeline",
                table: "CodeAgentSessions",
                column: "IsPipeline");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeAgentSessions");
        }
    }
}
