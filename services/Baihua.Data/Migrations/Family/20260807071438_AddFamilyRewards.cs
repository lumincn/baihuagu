using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baihua.Data.Migrations.Family
{
    /// <inheritdoc />
    public partial class AddFamilyRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FamilyRewards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConditionType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TargetValue = table.Column<int>(type: "INTEGER", nullable: false),
                    RewardName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RewardIcon = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyRewards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RewardClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RewardId = table.Column<int>(type: "INTEGER", nullable: false),
                    LearnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardClaims", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamilyRewards");

            migrationBuilder.DropTable(
                name: "RewardClaims");
        }
    }
}
