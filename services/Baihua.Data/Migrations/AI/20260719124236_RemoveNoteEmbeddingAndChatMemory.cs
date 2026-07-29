using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskRunner.Data.Migrations.AI
{
    /// <inheritdoc />
    public partial class RemoveNoteEmbeddingAndChatMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMemoryEntries");

            migrationBuilder.DropTable(
                name: "NoteEmbeddings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatMemoryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssistantContent = table.Column<string>(type: "TEXT", nullable: false),
                    AssistantSummary = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    Dimensions = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UserContent = table.Column<string>(type: "TEXT", nullable: false),
                    UserSummary = table.Column<string>(type: "TEXT", nullable: false),
                    VectorJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMemoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoteEmbeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    Dimensions = table.Column<int>(type: "INTEGER", nullable: false),
                    NotePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    VaultId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    VectorJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteEmbeddings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMemoryEntries_SessionId_Round",
                table: "ChatMemoryEntries",
                columns: new[] { "SessionId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_NoteEmbeddings_VaultId_NotePath",
                table: "NoteEmbeddings",
                columns: new[] { "VaultId", "NotePath" },
                unique: true);
        }
    }
}
