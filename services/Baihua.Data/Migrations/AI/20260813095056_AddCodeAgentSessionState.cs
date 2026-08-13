using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baihua.Data.Migrations.AI
{
    /// <inheritdoc />
    public partial class AddCodeAgentSessionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SessionStateJson",
                table: "CodeAgentSessions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionStateJson",
                table: "CodeAgentSessions");
        }
    }
}
