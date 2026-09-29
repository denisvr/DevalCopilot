using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunTokenStopThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ClaudeTokenStopThreshold",
                table: "runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CodexTokenStopThreshold",
                table: "runs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaudeTokenStopThreshold",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "CodexTokenStopThreshold",
                table: "runs");
        }
    }
}
