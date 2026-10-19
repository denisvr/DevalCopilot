using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0056: the database guards of an Agent supervision observation. SQLite runs every trigger inside the writer's own write
    /// transaction, atomically with the row it guards. They are the decisive exclusion at the durable seam; the Application's early
    /// reads only give a readable refusal before that point.
    /// <list type="bullet">
    /// <item><description>An observation may only be inserted for a still-Running Agent attempt of the same run, at its first phase
    /// and revision, open and without attention.</description></item>
    /// <item><description>The identity (run, attempt, host instance, observer and start time) never changes; a closed observation never
    /// changes again; the first attention is retained forever; the revision never decreases.</description></item>
    /// <item><description>A phase moves forward only. While the phase is unchanged its entry time and absolute deadline are fixed, so no
    /// heartbeat can extend a deadline.</description></item>
    /// </list>
    /// </summary>
    public partial class AddAgentSupervision
    {
        private const string TupleMessage = "agent_supervision.tuple_incoherent";
        private const string ImmutableMessage = "agent_supervision.immutable";

        private static readonly string[] TriggerNames =
        [
            "trg_agent_supervision_guard_insert",
            "trg_agent_supervision_immutable",
        ];

        private static string Rank(string prefix) =>
            "(CASE " + prefix + ".\"Phase\" WHEN 'Preparing' THEN 0 WHEN 'AwaitingAdapterResult' THEN 1 ELSE 2 END)";

        private static void CreateGuards(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_agent_supervision_guard_insert BEFORE INSERT ON agent_supervisions WHEN "
                + "NOT EXISTS (SELECT 1 FROM attempts AS a WHERE a.\"Id\" = NEW.\"AttemptId\" AND a.\"RunId\" = NEW.\"RunId\" "
                + "AND a.\"Kind\" = 'Agent' AND a.\"Status\" = 'Running' AND a.\"AgentProvider\" IS NOT NULL "
                + "AND a.\"AgentRole\" IS NOT NULL AND a.\"AgentResponseContract\" IS NOT NULL) "
                + "OR NEW.\"Revision\" <> 1 OR NEW.\"Phase\" <> 'Preparing' OR NEW.\"ClosedAtUtc\" IS NOT NULL "
                + "OR NEW.\"AttentionReason\" IS NOT NULL OR NEW.\"LastHeartbeatAtUtc\" <> NEW.\"StartedAtUtc\" "
                + "OR NEW.\"PhaseEnteredAtUtc\" <> NEW.\"StartedAtUtc\" "
                + $"BEGIN SELECT RAISE(ABORT, '{TupleMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_agent_supervision_immutable BEFORE UPDATE ON agent_supervisions WHEN "
                + "OLD.\"Id\" <> NEW.\"Id\" OR OLD.\"RunId\" <> NEW.\"RunId\" OR OLD.\"AttemptId\" <> NEW.\"AttemptId\" "
                + "OR OLD.\"HostInstanceId\" <> NEW.\"HostInstanceId\" OR OLD.\"ObserverId\" <> NEW.\"ObserverId\" "
                + "OR OLD.\"StartedAtUtc\" <> NEW.\"StartedAtUtc\" "
                + "OR OLD.\"ClosedAtUtc\" IS NOT NULL "
                + "OR NEW.\"Revision\" < OLD.\"Revision\" "
                + "OR (OLD.\"AttentionReason\" IS NOT NULL AND (NEW.\"AttentionReason\" IS NOT OLD.\"AttentionReason\" "
                + "OR NEW.\"AttentionAtUtc\" IS NOT OLD.\"AttentionAtUtc\")) "
                + "OR (NEW.\"Phase\" = OLD.\"Phase\" AND (NEW.\"PhaseEnteredAtUtc\" <> OLD.\"PhaseEnteredAtUtc\" "
                + "OR NEW.\"PhaseDeadlineUtc\" <> OLD.\"PhaseDeadlineUtc\")) "
                + "OR (NEW.\"Phase\" <> OLD.\"Phase\" AND " + Rank("NEW") + " <= " + Rank("OLD") + ") "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");
        }

        private static void DropGuards(MigrationBuilder migrationBuilder)
        {
            foreach (var name in TriggerNames)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {name};");
            }
        }
    }
}
