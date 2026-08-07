using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baihua.Data.Migrations.Family
{
    /// <inheritdoc />
    public partial class AddCheckinMakeupRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckinMakeupRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MakeupDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VaultId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckinMakeupRecords", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckinMakeupRecords");
        }
    }
}
