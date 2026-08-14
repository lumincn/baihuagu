using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baihua.Data.Migrations.Family
{
    /// <inheritdoc />
    public partial class AddTodoGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GoalId",
                table: "TodoItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "TodoItems",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TodoGoals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoGoals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_GoalId",
                table: "TodoItems",
                column: "GoalId");

            migrationBuilder.AddForeignKey(
                name: "FK_TodoItems_TodoGoals_GoalId",
                table: "TodoItems",
                column: "GoalId",
                principalTable: "TodoGoals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TodoItems_TodoGoals_GoalId",
                table: "TodoItems");

            migrationBuilder.DropTable(
                name: "TodoGoals");

            migrationBuilder.DropIndex(
                name: "IX_TodoItems_GoalId",
                table: "TodoItems");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "TodoItems");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "TodoItems");
        }
    }
}
