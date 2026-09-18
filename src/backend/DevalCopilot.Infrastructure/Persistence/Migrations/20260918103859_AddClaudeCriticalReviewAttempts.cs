using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClaudeCriticalReviewAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentInputCollaborationMessageId",
                table: "attempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentResponseContract",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            // Historical reconstruction, not fabrication: before this migration, the
            // CriticalReview contract did not exist, so every Agent attempt ever claimed was
            // claimed by ClaimAgent, whose only response contract was, and is, Proposal.
            migrationBuilder.Sql(
                """
                UPDATE attempts
                SET AgentResponseContract = 'Proposal'
                WHERE Kind = 'Agent' AND AgentResponseContract IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentInputCollaborationMessageId",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentResponseContract",
                table: "attempts");
        }
    }
}
