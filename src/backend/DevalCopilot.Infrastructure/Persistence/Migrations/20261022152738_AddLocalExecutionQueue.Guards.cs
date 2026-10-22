using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0062: the database guards of the queued local execution cycle. They extend the ADR-0060 guards with the new waiting state and
    /// add the queue's own bound, admission lifecycle and ordering guards. As in ADR-0060, SQLite runs every trigger inside the writer's
    /// own write transaction, atomically with the row it guards.
    /// <list type="bullet">
    /// <item><description>A waiting queued cycle (no reservation, no operation) holds the same unrelated-start and setting guards as any
    /// live sequence, but it owns no workspace: an Agent attempt of another run that is already executing there may still record its own
    /// result checkpoint, because the guard admits exactly the checkpoint an attempt of that workspace names as its result. A manual
    /// checkpoint capture is withheld.</description></item>
    /// <item><description>At most 64 queued cycles are live, a queued step exists only with its exact pinned pending admission, and an
    /// admission only moves Waiting to Reserved or Retired and Reserved to Consumed, Retired or Unresolved.</description></item>
    /// <item><description>The order counter never moves backwards and is never deleted; the mode and the coordination order of a sequence
    /// are immutable.</description></item>
    /// </list>
    /// </summary>
    public partial class AddLocalExecutionQueue
    {
        private const string BlockedMessage = "local_execution_sequence.manual_control_blocked";
        private const string NotEligibleMessage = "local_execution_sequence.run_not_eligible";
        private const string TupleMessage = "local_execution_sequence.tuple_incoherent";
        private const string ImmutableMessage = "local_execution_sequence.immutable";
        private const string BoundMessage = "local_execution_sequence.bound_exceeded";
        private const string QueueFullMessage = "local_execution_sequence.queue_full";

        /// <summary>The ADR-0060 triggers whose predicate gains the waiting state. They live on other tables and are recreated.</summary>
        private static readonly string[] LiveTriggerNames =
        [
            "trg_local_execution_guard_attempts",
            "trg_local_execution_guard_verification_executions",
            "trg_local_execution_guard_local_commit_operations",
            "trg_local_execution_guard_run_abandonment",
            "trg_local_execution_guard_run_settings",
            "trg_local_execution_guard_checkpoints",
            "trg_local_execution_guard_verification_commands_insert",
            "trg_local_execution_guard_verification_commands_update",
            "trg_local_execution_guard_verification_commands_delete",
        ];

        /// <summary>The ADR-0060 triggers of the rebuilt sequences table: the table rebuild drops them, so they are recreated.</summary>
        private static readonly string[] SequenceTriggerNames =
        [
            "trg_local_execution_guard_arming",
            "trg_local_execution_guard_tuple",
            "trg_local_execution_sequence_immutable",
        ];

        /// <summary>The ADR-0060 triggers of the steps and recipes tables, recreated unchanged: SQLite refuses to rename the rebuilt sequences
        /// table while any trigger still names it, so every trigger that names it is dropped before the rebuild and recreated after.</summary>
        private static readonly string[] StepTriggerNames =
        [
            "trg_local_execution_recipe_immutable",
            "trg_local_execution_step_bounds",
            "trg_local_execution_step_immutable",
        ];

        private static readonly string[] QueueTriggerNames =
        [
            "trg_local_execution_queue_bound",
            "trg_local_execution_admission_insert_shape",
            "trg_local_execution_admission_insert_bound",
            "trg_local_execution_admission_immutable",
            "trg_local_execution_step_admission",
            "trg_local_execution_counter_monotonic",
            "trg_local_execution_counter_retained",
        ];

        private const string AttemptExitProof =
            "COALESCE(a.\"AgentProcessOutcome\" = 'Exited' AND a.\"AgentProcessExitCode\" IS NOT NULL "
            + "AND a.\"AgentProcessDuration\" >= 0, 0)";

        private const string ExecutionExitProof = "COALESCE(v.\"Outcome\" = 'Exited' AND v.\"ExitCode\" IS NOT NULL, 0)";

        /// <summary>
        /// The sequence is live for the given scope: its automatic authority can advance, drain or (queued) wait, a step preparation is
        /// reserved or unresolved, a linked operation is still Running, or a linked dispatched operation lacks coherent Exited evidence.
        /// <paramref name="includeWaiting"/> false is the ADR-0060 predicate (an owning sequence); true also holds a waiting queued cycle.
        /// </summary>
        private static string Live(string scopeColumn, string scopeValue, bool includeWaiting) =>
            "EXISTS (SELECT 1 FROM local_execution_sequences AS s WHERE s.\"" + scopeColumn + "\" = " + scopeValue + " AND ("
            + (includeWaiting
                ? "s.\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'WaitingForAdmission')"
                : "s.\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping')")
            + " OR EXISTS ("
            + "SELECT 1 FROM local_execution_sequence_steps AS t WHERE t.\"SequenceId\" = s.\"Id\" AND ("
            + "t.\"State\" IN ('Reserved', 'Unresolved') OR (t.\"OperationId\" IS NOT NULL AND ("
            + "(t.\"Kind\" <> 'Verification' AND EXISTS (SELECT 1 FROM attempts AS a WHERE a.\"Id\" = t.\"OperationId\" AND ("
            + "a.\"Status\" = 'Running' OR (a.\"AgentDispatchedAtUtc\" IS NOT NULL AND NOT " + AttemptExitProof + ")))) "
            + "OR (t.\"Kind\" = 'Verification' AND EXISTS (SELECT 1 FROM verification_executions AS v WHERE v.\"Id\" = t.\"OperationId\" AND ("
            + "v.\"Status\" = 'Running' OR (v.\"DispatchedAtUtc\" IS NOT NULL AND NOT " + ExecutionExitProof + "))))"
            + "))))))";

        private static string ReservedOperation(string kindPredicate, string scopeColumn, string scopeValue, string idValue) =>
            "EXISTS (SELECT 1 FROM local_execution_sequence_steps AS t JOIN local_execution_sequences AS s ON s.\"Id\" = t.\"SequenceId\" "
            + "WHERE t.\"ReservedOperationId\" = " + idValue + " AND t.\"State\" IN ('Reserved', 'Claimed') AND " + kindPredicate
            + " AND s.\"" + scopeColumn + "\" = " + scopeValue + ")";

        private static void CreateGuards(MigrationBuilder migrationBuilder)
        {
            CreateLiveGuards(migrationBuilder, includeWaiting: true);
            CreateSequenceGuards(migrationBuilder, queued: true);
            CreateStepGuards(migrationBuilder);

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_queue_bound BEFORE INSERT ON local_execution_sequences WHEN "
                + "NEW.\"AdmissionMode\" = 'WaitForAdmission' AND (SELECT COUNT(*) FROM local_execution_sequences AS s "
                + "WHERE s.\"AdmissionMode\" = 'WaitForAdmission' AND s.\"State\" IN "
                + "('WaitingForAdmission', 'PreparingStep', 'AwaitingStepResult', 'Stopping')) >= 64 "
                + $"BEGIN SELECT RAISE(ABORT, '{QueueFullMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_admission_insert_shape BEFORE INSERT ON local_execution_admissions WHEN "
                + "NEW.\"State\" <> 'Waiting' OR NOT EXISTS (SELECT 1 FROM local_execution_sequences AS s WHERE s.\"Id\" = NEW.\"SequenceId\" "
                + "AND s.\"AdmissionMode\" = 'WaitForAdmission' AND s.\"State\" IN ('WaitingForAdmission', 'AwaitingStepResult') "
                + "AND s.\"WorkspaceId\" IS NOT NULL) "
                + $"BEGIN SELECT RAISE(ABORT, '{TupleMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_admission_insert_bound BEFORE INSERT ON local_execution_admissions WHEN "
                + "NEW.\"Ordinal\" >= (SELECT 3 * s.\"RecipeCount\" + 6 FROM local_execution_sequences AS s WHERE s.\"Id\" = NEW.\"SequenceId\") "
                + "OR (NEW.\"RecipeOrdinal\" IS NOT NULL AND NEW.\"RecipeOrdinal\" >= "
                + "(SELECT s.\"RecipeCount\" FROM local_execution_sequences AS s WHERE s.\"Id\" = NEW.\"SequenceId\")) "
                + $"BEGIN SELECT RAISE(ABORT, '{BoundMessage}'); END;");

            // Identity and order are immutable, an admission only moves forward, and a reservation, a consumption and a retirement each
            // name the exact step of the same sequence and ordinal. A step may already read Claimed or Revoked when the admission update
            // is ordered after the step's own update inside the same save.
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_admission_immutable BEFORE UPDATE ON local_execution_admissions WHEN "
                + "OLD.\"Id\" <> NEW.\"Id\" OR OLD.\"SequenceId\" <> NEW.\"SequenceId\" OR OLD.\"AdmissionNumber\" <> NEW.\"AdmissionNumber\" "
                + "OR OLD.\"Ordinal\" <> NEW.\"Ordinal\" OR OLD.\"Kind\" <> NEW.\"Kind\" OR OLD.\"PassNumber\" <> NEW.\"PassNumber\" "
                + "OR OLD.\"RecipeOrdinal\" IS NOT NEW.\"RecipeOrdinal\" OR OLD.\"PredecessorStepId\" IS NOT NEW.\"PredecessorStepId\" "
                + "OR OLD.\"SourceCheckpointId\" <> NEW.\"SourceCheckpointId\" OR OLD.\"SourceFingerprintSha256\" <> NEW.\"SourceFingerprintSha256\" "
                + "OR OLD.\"ReportMessageId\" IS NOT NEW.\"ReportMessageId\" OR OLD.\"SubjectAttemptId\" IS NOT NEW.\"SubjectAttemptId\" "
                + "OR OLD.\"EnqueuedAtUtc\" <> NEW.\"EnqueuedAtUtc\" "
                + "OR (OLD.\"StepId\" IS NOT NULL AND OLD.\"StepId\" IS NOT NEW.\"StepId\") "
                + "OR OLD.\"State\" IN ('Consumed', 'Retired', 'Unresolved') "
                + "OR (OLD.\"State\" = 'Waiting' AND NEW.\"State\" NOT IN ('Waiting', 'Reserved', 'Retired')) "
                + "OR (OLD.\"State\" = 'Reserved' AND NEW.\"State\" NOT IN ('Consumed', 'Retired', 'Unresolved')) "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_step_admission BEFORE INSERT ON local_execution_sequence_steps WHEN "
                + "EXISTS (SELECT 1 FROM local_execution_sequences AS s WHERE s.\"Id\" = NEW.\"SequenceId\" AND s.\"AdmissionMode\" = 'WaitForAdmission') "
                + "AND NOT EXISTS (SELECT 1 FROM local_execution_admissions AS a WHERE a.\"SequenceId\" = NEW.\"SequenceId\" "
                + "AND a.\"Ordinal\" = NEW.\"Ordinal\" AND a.\"State\" IN ('Waiting', 'Reserved') AND a.\"Kind\" = NEW.\"Kind\" "
                + "AND a.\"PassNumber\" = NEW.\"PassNumber\" AND a.\"RecipeOrdinal\" IS NEW.\"RecipeOrdinal\" "
                + "AND a.\"PredecessorStepId\" IS NEW.\"PredecessorStepId\" AND a.\"SourceCheckpointId\" = NEW.\"SourceCheckpointId\" "
                + "AND a.\"SourceFingerprintSha256\" = NEW.\"SourceFingerprintSha256\" AND a.\"ReportMessageId\" IS NEW.\"ReportMessageId\" "
                + "AND a.\"SubjectAttemptId\" IS NEW.\"SubjectAttemptId\") "
                + $"BEGIN SELECT RAISE(ABORT, '{TupleMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_counter_monotonic BEFORE UPDATE ON local_execution_order_counters WHEN "
                + "NEW.\"LastNumber\" < OLD.\"LastNumber\" OR NEW.\"Id\" <> OLD.\"Id\" "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_counter_retained BEFORE DELETE ON local_execution_order_counters "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");
        }

        /// <summary>Every trigger that names the sequences table, dropped before its rebuild (Up and Down alike).</summary>
        private static void DropAllGuards(MigrationBuilder migrationBuilder)
        {
            DropTriggers(migrationBuilder, QueueTriggerNames);
            DropTriggers(migrationBuilder, LiveTriggerNames);
            DropTriggers(migrationBuilder, SequenceTriggerNames);
            DropTriggers(migrationBuilder, StepTriggerNames);
        }

        /// <summary>Down: after the table rebuild, the ADR-0060 definitions of every trigger this migration replaced or lost.</summary>
        private static void RestorePreviousGuards(MigrationBuilder migrationBuilder)
        {
            CreateLiveGuards(migrationBuilder, includeWaiting: false);
            CreateSequenceGuards(migrationBuilder, queued: false);
            CreateStepGuards(migrationBuilder);
        }

        private static void DropTriggers(MigrationBuilder migrationBuilder, string[] names)
        {
            foreach (var name in names)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {name};");
            }
        }

        private static void CreateLiveGuards(MigrationBuilder migrationBuilder, bool includeWaiting)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_attempts BEFORE INSERT ON attempts WHEN "
                + Live("RunId", "NEW.\"RunId\"", includeWaiting)
                + " AND NOT " + ReservedOperation("t.\"Kind\" <> 'Verification'", "RunId", "NEW.\"RunId\"", "NEW.\"Id\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_executions BEFORE INSERT ON verification_executions WHEN "
                + Live("WorkspaceId", "NEW.\"GitWorkspaceId\"", includeWaiting)
                + " AND NOT " + ReservedOperation("t.\"Kind\" = 'Verification'", "WorkspaceId", "NEW.\"GitWorkspaceId\"", "NEW.\"Id\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_local_commit_operations BEFORE INSERT ON local_commit_operations WHEN "
                + Live("WorkspaceId", "NEW.\"GitWorkspaceId\"", includeWaiting)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_run_abandonment BEFORE UPDATE OF \"Lifecycle\" ON runs WHEN "
                + "NEW.\"Lifecycle\" = 'Abandoned' AND OLD.\"Lifecycle\" <> 'Abandoned' AND "
                + Live("RunId", "NEW.\"Id\"", includeWaiting)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_run_settings BEFORE UPDATE OF \"RequestedCodexModel\", \"RequestedCodexEffort\", "
                + "\"RequestedClaudeModel\", \"RequestedClaudeEffort\", \"RequestedClaudeMaxTurns\", \"CodexAccountUsageStopPercent\", "
                + "\"CodexTokenStopThreshold\", \"ClaudeTokenStopThreshold\" ON runs WHEN ("
                + "OLD.\"RequestedCodexModel\" IS NOT NEW.\"RequestedCodexModel\" OR OLD.\"RequestedCodexEffort\" IS NOT NEW.\"RequestedCodexEffort\" "
                + "OR OLD.\"RequestedClaudeModel\" IS NOT NEW.\"RequestedClaudeModel\" OR OLD.\"RequestedClaudeEffort\" IS NOT NEW.\"RequestedClaudeEffort\" "
                + "OR OLD.\"RequestedClaudeMaxTurns\" IS NOT NEW.\"RequestedClaudeMaxTurns\" "
                + "OR OLD.\"CodexAccountUsageStopPercent\" IS NOT NEW.\"CodexAccountUsageStopPercent\" "
                + "OR OLD.\"CodexTokenStopThreshold\" IS NOT NEW.\"CodexTokenStopThreshold\" "
                + "OR OLD.\"ClaudeTokenStopThreshold\" IS NOT NEW.\"ClaudeTokenStopThreshold\") AND "
                + Live("RunId", "NEW.\"Id\"", includeWaiting)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            // A manual checkpoint is withheld; only a result checkpoint of a linked, still Running Agent operation of the workspace is
            // admitted. ADR-0062: a workspace that is only waited for owns no execution, so the result checkpoint an Agent attempt of
            // that workspace names as its own (another run's already executing attempt) is admitted as well; a capture is not.
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_checkpoints BEFORE INSERT ON git_checkpoints WHEN "
                + Live("WorkspaceId", "NEW.\"WorkspaceId\"", includeWaiting)
                + " AND NOT EXISTS (SELECT 1 FROM local_execution_sequence_steps AS t JOIN local_execution_sequences AS s ON s.\"Id\" = t.\"SequenceId\" "
                + "JOIN attempts AS a ON a.\"Id\" = t.\"OperationId\" WHERE s.\"WorkspaceId\" = NEW.\"WorkspaceId\" AND t.\"Kind\" <> 'Verification' "
                + "AND t.\"State\" = 'Claimed' AND (a.\"Status\" = 'Running' OR a.\"AgentResultGitCheckpointId\" = NEW.\"Id\"))"
                + (includeWaiting
                    ? " AND NOT (NOT " + Live("WorkspaceId", "NEW.\"WorkspaceId\"", includeWaiting: false)
                        + " AND EXISTS (SELECT 1 FROM attempts AS r WHERE r.\"AgentGitWorkspaceId\" = NEW.\"WorkspaceId\" "
                        + "AND r.\"AgentResultGitCheckpointId\" = NEW.\"Id\"))"
                    : string.Empty)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_commands_insert BEFORE INSERT ON verification_commands WHEN "
                + Live("ProjectId", "NEW.\"ProjectId\"", includeWaiting)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_commands_update BEFORE UPDATE ON verification_commands WHEN "
                + Live("ProjectId", "OLD.\"ProjectId\"", includeWaiting)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_commands_delete BEFORE DELETE ON verification_commands WHEN "
                + Live("ProjectId", "OLD.\"ProjectId\"", includeWaiting)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");
        }

        private static void CreateStepGuards(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_recipe_immutable BEFORE UPDATE ON local_execution_sequence_recipes "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");

            // The fixed bounds: at most 3 * recipes + 6 steps, six Agent claims and the pinned correction allowance. A revoked step
            // consumed nothing and never counts.
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_step_bounds BEFORE INSERT ON local_execution_sequence_steps WHEN "
                + "NEW.\"Ordinal\" >= (SELECT 3 * s.\"RecipeCount\" + 6 FROM local_execution_sequences AS s WHERE s.\"Id\" = NEW.\"SequenceId\") "
                + "OR (NEW.\"RecipeOrdinal\" IS NOT NULL AND NEW.\"RecipeOrdinal\" >= (SELECT s.\"RecipeCount\" FROM local_execution_sequences AS s WHERE s.\"Id\" = NEW.\"SequenceId\")) "
                + "OR (NEW.\"Kind\" <> 'Verification' AND (SELECT COUNT(*) FROM local_execution_sequence_steps AS t "
                + "WHERE t.\"SequenceId\" = NEW.\"SequenceId\" AND t.\"Kind\" <> 'Verification' AND t.\"State\" <> 'Revoked') >= 6) "
                + "OR (NEW.\"Kind\" IN ('ReviewCorrection', 'DiagnosisCorrection') AND (SELECT COUNT(*) FROM local_execution_sequence_steps AS t "
                + "WHERE t.\"SequenceId\" = NEW.\"SequenceId\" AND t.\"Kind\" IN ('ReviewCorrection', 'DiagnosisCorrection') AND t.\"State\" <> 'Revoked') "
                + ">= (SELECT s.\"CorrectionAllowance\" FROM local_execution_sequences AS s WHERE s.\"Id\" = NEW.\"SequenceId\")) "
                + $"BEGIN SELECT RAISE(ABORT, '{BoundMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_step_immutable BEFORE UPDATE ON local_execution_sequence_steps WHEN "
                + "OLD.\"Id\" <> NEW.\"Id\" OR OLD.\"SequenceId\" <> NEW.\"SequenceId\" OR OLD.\"Ordinal\" <> NEW.\"Ordinal\" "
                + "OR OLD.\"Kind\" <> NEW.\"Kind\" OR OLD.\"PassNumber\" <> NEW.\"PassNumber\" "
                + "OR OLD.\"RecipeOrdinal\" IS NOT NEW.\"RecipeOrdinal\" OR OLD.\"PredecessorStepId\" IS NOT NEW.\"PredecessorStepId\" "
                + "OR OLD.\"SourceCheckpointId\" <> NEW.\"SourceCheckpointId\" OR OLD.\"SourceFingerprintSha256\" <> NEW.\"SourceFingerprintSha256\" "
                + "OR OLD.\"ReportMessageId\" IS NOT NEW.\"ReportMessageId\" OR OLD.\"SubjectAttemptId\" IS NOT NEW.\"SubjectAttemptId\" "
                + "OR OLD.\"ReservedOperationId\" <> NEW.\"ReservedOperationId\" OR OLD.\"PreparationOwnerToken\" <> NEW.\"PreparationOwnerToken\" "
                + "OR OLD.\"PreparationReservedAtUtc\" <> NEW.\"PreparationReservedAtUtc\" "
                + "OR OLD.\"PreparationDeadlineUtc\" <> NEW.\"PreparationDeadlineUtc\" "
                + "OR (OLD.\"OperationId\" IS NOT NULL AND (OLD.\"OperationId\" IS NOT NEW.\"OperationId\" "
                + "OR OLD.\"ClaimedAtUtc\" IS NOT NEW.\"ClaimedAtUtc\" OR OLD.\"ResultDeadlineUtc\" IS NOT NEW.\"ResultDeadlineUtc\")) "
                + "OR OLD.\"State\" IN ('Concluded', 'Revoked', 'Unresolved') "
                + "OR (OLD.\"State\" = 'Claimed' AND NEW.\"State\" <> 'Concluded') "
                + "OR (OLD.\"State\" = 'Reserved' AND NEW.\"State\" = 'Concluded') "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");
        }

        private static void CreateSequenceGuards(MigrationBuilder migrationBuilder, bool queued)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_arming BEFORE INSERT ON local_execution_sequences WHEN "
                + "NOT EXISTS (SELECT 1 FROM runs AS r WHERE r.\"Id\" = NEW.\"RunId\" AND r.\"Lifecycle\" = 'Running') "
                + "OR EXISTS (SELECT 1 FROM attempts AS a WHERE a.\"RunId\" = NEW.\"RunId\" AND (a.\"Status\" = 'Running' "
                + "OR a.\"AgentResponseContract\" IN ('ImplementationReport', 'ReviewCorrection'))) "
                + "OR EXISTS (SELECT 1 FROM local_commit_operations AS o WHERE o.\"RunId\" = NEW.\"RunId\") "
                + "OR EXISTS (SELECT 1 FROM planning_sequences AS p WHERE p.\"RunId\" = NEW.\"RunId\" "
                + "AND p.\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping')) "
                + $"BEGIN SELECT RAISE(ABORT, '{NotEligibleMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_tuple BEFORE INSERT ON local_execution_sequences WHEN "
                + "NOT EXISTS (SELECT 1 FROM runs AS r WHERE r.\"Id\" = NEW.\"RunId\" AND r.\"ProjectId\" = NEW.\"ProjectId\") "
                + "OR NOT EXISTS (SELECT 1 FROM git_workspaces AS w WHERE w.\"Id\" = NEW.\"WorkspaceId\" AND w.\"ProjectId\" = NEW.\"ProjectId\") "
                + "OR NOT EXISTS (SELECT 1 FROM git_checkpoints AS c WHERE c.\"Id\" = NEW.\"CheckpointId\" AND c.\"WorkspaceId\" = NEW.\"WorkspaceId\") "
                + "OR NOT EXISTS (SELECT 1 FROM collaboration_messages AS m WHERE m.\"Id\" = NEW.\"PlanProposalMessageId\" AND m.\"RunId\" = NEW.\"RunId\") "
                + $"BEGIN SELECT RAISE(ABORT, '{TupleMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_sequence_immutable BEFORE UPDATE ON local_execution_sequences WHEN "
                + "OLD.\"Id\" <> NEW.\"Id\" OR OLD.\"RunId\" <> NEW.\"RunId\" OR OLD.\"ProjectId\" <> NEW.\"ProjectId\" "
                + "OR OLD.\"WorkspaceId\" <> NEW.\"WorkspaceId\" OR OLD.\"CheckpointId\" <> NEW.\"CheckpointId\" "
                + "OR OLD.\"FingerprintSha256\" <> NEW.\"FingerprintSha256\" OR OLD.\"PlanProposalMessageId\" <> NEW.\"PlanProposalMessageId\" "
                + "OR OLD.\"PlanAuthority\" <> NEW.\"PlanAuthority\" OR OLD.\"ConfirmationSha256\" <> NEW.\"ConfirmationSha256\" OR OLD.\"PlanInputsSha256\" <> NEW.\"PlanInputsSha256\" "
                + "OR OLD.\"SettingsSnapshot\" <> NEW.\"SettingsSnapshot\" OR OLD.\"RecipeCount\" <> NEW.\"RecipeCount\" "
                + "OR OLD.\"CorrectionAllowance\" <> NEW.\"CorrectionAllowance\" OR OLD.\"ArmedAtUtc\" <> NEW.\"ArmedAtUtc\" "
                + "OR OLD.\"AdvancementDeadlineUtc\" <> NEW.\"AdvancementDeadlineUtc\" "
                + (queued
                    ? "OR OLD.\"AdmissionMode\" <> NEW.\"AdmissionMode\" OR OLD.\"CoordinationNumber\" <> NEW.\"CoordinationNumber\" "
                        + "OR (NEW.\"State\" = 'WaitingForAdmission' AND OLD.\"State\" NOT IN ('WaitingForAdmission', 'AwaitingStepResult')) "
                    : string.Empty)
                + "OR OLD.\"State\" IN ('ReadyForHumanApproval', 'HumanEscalation', 'Stopped', 'NeedsAttention', 'Interrupted') "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");
        }
    }
}
