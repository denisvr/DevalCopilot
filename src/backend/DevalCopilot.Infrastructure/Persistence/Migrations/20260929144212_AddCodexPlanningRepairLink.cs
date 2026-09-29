using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodexPlanningRepairLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentRepairSourceAttemptId",
                table: "attempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_attempts_agent_repair_source",
                table: "attempts",
                column: "AgentRepairSourceAttemptId",
                unique: true,
                filter: "\"AgentRepairSourceAttemptId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_attempts_attempts_AgentRepairSourceAttemptId",
                table: "attempts",
                column: "AgentRepairSourceAttemptId",
                principalTable: "attempts",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_attempts_attempts_AgentRepairSourceAttemptId",
                table: "attempts");

            migrationBuilder.DropIndex(
                name: "ix_attempts_agent_repair_source",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "AgentRepairSourceAttemptId",
                table: "attempts");
        }
    }
}
