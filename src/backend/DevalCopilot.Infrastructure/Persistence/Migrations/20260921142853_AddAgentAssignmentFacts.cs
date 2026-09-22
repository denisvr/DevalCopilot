using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentAssignmentFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentAdapterContractVersion",
                table: "attempts",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentObservedEffort",
                table: "attempts",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentObservedModel",
                table: "attempts",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentPermissionProfile",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentRequestedEffort",
                table: "attempts",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentRequestedModel",
                table: "attempts",
                type: "TEXT",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentAdapterContractVersion",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentObservedEffort",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentObservedModel",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentPermissionProfile",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentRequestedEffort",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentRequestedModel",
                table: "attempts");
        }
    }
}
