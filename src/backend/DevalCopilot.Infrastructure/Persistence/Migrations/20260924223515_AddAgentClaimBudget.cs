using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentClaimBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaximumAgentAttempts",
                table: "runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 16);

            migrationBuilder.AddColumn<int>(
                name: "AgentBudgetSlot",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            // Historical reconstruction, not fabrication: before this migration, an Agent
            // attempt's claim order within its run was never assigned a permanent budget slot.
            // Every existing Agent attempt is truthfully backfilled a deterministic 1-based slot
            // in its own run, ordered by AttemptNumber — the exact order those attempts were
            // actually claimed in. Simulated and Process attempts are never touched; their slot
            // stays null.
            migrationBuilder.Sql(
                """
                UPDATE attempts
                SET AgentBudgetSlot = (
                    SELECT COUNT(*)
                    FROM attempts AS earlier
                    WHERE earlier.RunId = attempts.RunId
                        AND earlier.Kind = 'Agent'
                        AND earlier.AttemptNumber <= attempts.AttemptNumber
                )
                WHERE attempts.Kind = 'Agent';
                """);

            // A run whose real historical Agent-attempt count already exceeds the new default
            // ceiling of 16 must never retroactively appear to have violated its own budget: its
            // maximum is truthfully raised to at least that already-consumed count, never lowered
            // below it. A run with 16 or fewer historical Agent attempts keeps the ordinary
            // default applied by the column's own DEFAULT above.
            migrationBuilder.Sql(
                """
                UPDATE runs
                SET MaximumAgentAttempts = MAX(16, (
                    SELECT COUNT(*)
                    FROM attempts
                    WHERE attempts.RunId = runs.Id AND attempts.Kind = 'Agent'
                ));
                """);

            migrationBuilder.CreateIndex(
                name: "ix_attempts_run_id_agent_budget_slot",
                table: "attempts",
                columns: new[] { "RunId", "AgentBudgetSlot" },
                unique: true,
                filter: "\"AgentBudgetSlot\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_attempts_run_id_agent_budget_slot",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "MaximumAgentAttempts",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "AgentBudgetSlot",
                table: "attempts");
        }
    }
}
