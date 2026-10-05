using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodexAccountUsageStop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodexAccountUsageStopPercent",
                table: "runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentAccountUsageDecisionSnapshot",
                table: "attempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentCodexAccountUsageStopPercent",
                table: "attempts",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodexAccountUsageStopPercent",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "AgentAccountUsageDecisionSnapshot",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentCodexAccountUsageStopPercent",
                table: "attempts");
        }
    }
}
