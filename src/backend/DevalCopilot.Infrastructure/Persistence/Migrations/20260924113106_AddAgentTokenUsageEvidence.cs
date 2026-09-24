using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTokenUsageEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgentCacheCreationInputTokens",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgentCacheReadInputTokens",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgentInputTokens",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgentOutputTokens",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentTokenUsageSchemaVersion",
                table: "attempts",
                type: "TEXT",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentCacheCreationInputTokens",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentCacheReadInputTokens",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentInputTokens",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentOutputTokens",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentTokenUsageSchemaVersion",
                table: "attempts");
        }
    }
}
