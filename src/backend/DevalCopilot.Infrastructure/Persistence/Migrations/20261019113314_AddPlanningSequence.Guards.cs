using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0055: the database guards of the planning sequence. SQLite runs every trigger inside the writer's own write transaction,
    /// atomically with the row it guards, exactly as the local-commit seam guards do. They are the decisive exclusion at each durable
    /// seam; the Application's early reads only give a readable refusal before that point.
    /// <list type="bullet">
    /// <item><description>While a sequence's automatic authority is live (it can advance, or drains a claimed step) or one of its linked
    /// attempts is still Running, no attempt of its run may be inserted unless it is the exact reserved attempt of a still-Reserved step,
    /// no verification execution or local commit may be admitted for its workspace, and its run may not become Abandoned.</description></item>
    /// <item><description>Arming may only bind a pristine Created run to a coherent project, workspace and checkpoint.</description></item>
    /// <item><description>The authorization tuple and the step identity are immutable, an ended sequence never changes again (no rearm,
    /// resume or retarget) and a step only moves Reserved to Claimed, Revoked or Unresolved and Claimed to Concluded.</description></item>
    /// </list>
    /// </summary>
    public partial class AddPlanningSequence
    {
        private const string BlockedMessage = "planning_sequence.manual_control_blocked";
        private const string NotPristineMessage = "planning_sequence.run_not_pristine";
        private const string TupleMessage = "planning_sequence.tuple_incoherent";
        private const string ImmutableMessage = "planning_sequence.immutable";

        private static readonly string[] TriggerNames =
        [
            "trg_planning_sequence_guard_attempts",
            "trg_planning_sequence_guard_verification_executions",
            "trg_planning_sequence_guard_local_commit_operations",
            "trg_planning_sequence_guard_run_abandonment",
            "trg_planning_sequence_guard_arming",
            "trg_planning_sequence_guard_tuple",
            "trg_planning_sequence_immutable",
            "trg_planning_sequence_step_immutable",
        ];

        /// <summary>The sequence is live for the given scope: its automatic authority can advance or drain, or a linked attempt is
        /// still Running (an expired or ended sequence never releases an unresolved attempt).</summary>
        private static string Live(string scopeColumn, string scopeValue) =>
            "EXISTS (SELECT 1 FROM planning_sequences AS s WHERE s.\"" + scopeColumn + "\" = " + scopeValue + " AND ("
            + "s.\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping') OR EXISTS ("
            + "SELECT 1 FROM planning_sequence_steps AS t JOIN attempts AS a ON a.\"Id\" = t.\"ReservedAttemptId\" "
            + "WHERE t.\"SequenceId\" = s.\"Id\" AND a.\"Status\" = 'Running')))";

        private static void CreateGuards(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_planning_sequence_guard_attempts BEFORE INSERT ON attempts WHEN "
                + Live("RunId", "NEW.\"RunId\"")
                + " AND NOT EXISTS (SELECT 1 FROM planning_sequence_steps AS t JOIN planning_sequences AS s ON s.\"Id\" = t.\"SequenceId\" "
                + "WHERE t.\"ReservedAttemptId\" = NEW.\"Id\" AND t.\"State\" = 'Reserved' AND s.\"RunId\" = NEW.\"RunId\") "
                + $"BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_planning_sequence_guard_verification_executions BEFORE INSERT ON verification_executions WHEN "
                + Live("WorkspaceId", "NEW.\"GitWorkspaceId\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_planning_sequence_guard_local_commit_operations BEFORE INSERT ON local_commit_operations WHEN "
                + Live("WorkspaceId", "NEW.\"GitWorkspaceId\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_planning_sequence_guard_run_abandonment BEFORE UPDATE OF \"Lifecycle\" ON runs WHEN "
                + "NEW.\"Lifecycle\" = 'Abandoned' AND OLD.\"Lifecycle\" <> 'Abandoned' AND "
                + Live("RunId", "NEW.\"Id\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_planning_sequence_guard_arming BEFORE INSERT ON planning_sequences WHEN "
                + "NOT EXISTS (SELECT 1 FROM runs AS r WHERE r.\"Id\" = NEW.\"RunId\" AND r.\"Lifecycle\" = 'Created') "
                + "OR EXISTS (SELECT 1 FROM attempts AS a WHERE a.\"RunId\" = NEW.\"RunId\") "
                + "OR EXISTS (SELECT 1 FROM collaboration_messages AS m WHERE m.\"RunId\" = NEW.\"RunId\") "
                + "OR EXISTS (SELECT 1 FROM local_commit_operations AS o WHERE o.\"RunId\" = NEW.\"RunId\") "
                + $"BEGIN SELECT RAISE(ABORT, '{NotPristineMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_planning_sequence_guard_tuple BEFORE INSERT ON planning_sequences WHEN "
                + "NOT EXISTS (SELECT 1 FROM runs AS r WHERE r.\"Id\" = NEW.\"RunId\" AND r.\"ProjectId\" = NEW.\"ProjectId\") "
                + "OR NOT EXISTS (SELECT 1 FROM git_workspaces AS w WHERE w.\"Id\" = NEW.\"WorkspaceId\" AND w.\"ProjectId\" = NEW.\"ProjectId\") "
                + "OR NOT EXISTS (SELECT 1 FROM git_checkpoints AS c WHERE c.\"Id\" = NEW.\"CheckpointId\" AND c.\"WorkspaceId\" = NEW.\"WorkspaceId\") "
                + $"BEGIN SELECT RAISE(ABORT, '{TupleMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_planning_sequence_immutable BEFORE UPDATE ON planning_sequences WHEN "
                + "OLD.\"Id\" <> NEW.\"Id\" OR OLD.\"RunId\" <> NEW.\"RunId\" OR OLD.\"ProjectId\" <> NEW.\"ProjectId\" "
                + "OR OLD.\"WorkspaceId\" <> NEW.\"WorkspaceId\" OR OLD.\"CheckpointId\" <> NEW.\"CheckpointId\" "
                + "OR OLD.\"FingerprintSha256\" <> NEW.\"FingerprintSha256\" OR OLD.\"ObjectiveSha256\" <> NEW.\"ObjectiveSha256\" "
                + "OR OLD.\"ArmedAtUtc\" <> NEW.\"ArmedAtUtc\" "
                + "OR OLD.\"State\" IN ('ReadyForImplementation', 'AwaitingHumanDecision', 'Stopped', 'NeedsAttention', 'Interrupted') "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_planning_sequence_step_immutable BEFORE UPDATE ON planning_sequence_steps WHEN "
                + "OLD.\"Id\" <> NEW.\"Id\" OR OLD.\"SequenceId\" <> NEW.\"SequenceId\" OR OLD.\"Ordinal\" <> NEW.\"Ordinal\" "
                + "OR OLD.\"Role\" <> NEW.\"Role\" OR OLD.\"ReservedAttemptId\" <> NEW.\"ReservedAttemptId\" "
                + "OR OLD.\"PreparationOwnerToken\" <> NEW.\"PreparationOwnerToken\" "
                + "OR OLD.\"PreparationReservedAtUtc\" <> NEW.\"PreparationReservedAtUtc\" "
                + "OR OLD.\"PreparationDeadlineUtc\" <> NEW.\"PreparationDeadlineUtc\" "
                + "OR OLD.\"PredecessorAttemptId\" IS NOT NEW.\"PredecessorAttemptId\" "
                + "OR OLD.\"InputProposalMessageId\" IS NOT NEW.\"InputProposalMessageId\" "
                + "OR OLD.\"State\" IN ('Concluded', 'Revoked', 'Unresolved') "
                + "OR (OLD.\"State\" = 'Claimed' AND NEW.\"State\" <> 'Concluded') "
                + "OR (OLD.\"State\" = 'Reserved' AND NEW.\"State\" = 'Concluded') "
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
