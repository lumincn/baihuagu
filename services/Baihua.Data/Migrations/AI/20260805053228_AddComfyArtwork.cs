using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baihua.Data.Migrations.AI
{
    /// <inheritdoc />
    public partial class AddComfyArtwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComfyArtworks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParamsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Subfolder = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false, defaultValue: ""),
                    FileType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "output"),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsSuccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComfyArtworks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComfyArtworks_CreatedAt",
                table: "ComfyArtworks",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ComfyArtworks_Kind",
                table: "ComfyArtworks",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_ComfyArtworks_PromptId",
                table: "ComfyArtworks",
                column: "PromptId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComfyArtworks");
        }
    }
}
