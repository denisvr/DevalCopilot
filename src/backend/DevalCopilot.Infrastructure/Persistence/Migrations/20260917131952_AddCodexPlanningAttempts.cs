using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodexPlanningAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentCheckpointFingerprintSha256",
                table: "attempts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AgentContextManifestArtifactId",
                table: "attempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgentDispatchedAtUtc",
                table: "attempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentExpectedMessageType",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AgentGitCheckpointId",
                table: "attempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AgentGitWorkspaceId",
                table: "attempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgentMaxBytesPerStream",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgentMaxTotalCapturedBytes",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentOutcome",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentProtocolVersion",
                table: "attempts",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentProvider",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentProviderSessionId",
                table: "attempts",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentRole",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AgentTimeout",
                table: "attempts",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentCheckpointFingerprintSha256",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentContextManifestArtifactId",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentDispatchedAtUtc",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentExpectedMessageType",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentGitCheckpointId",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentGitWorkspaceId",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentMaxBytesPerStream",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentMaxTotalCapturedBytes",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentOutcome",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentProtocolVersion",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentProvider",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentProviderSessionId",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentRole",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentTimeout",
                table: "attempts");
        }
    }
}
