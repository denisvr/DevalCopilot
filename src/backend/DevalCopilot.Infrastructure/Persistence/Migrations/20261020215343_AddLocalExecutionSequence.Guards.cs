using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0060: the database guards of the local execution sequence. SQLite runs every trigger inside the writer's own write
    /// transaction, atomically with the row it guards, exactly as the planning-sequence and local-commit seam guards do. They are the
    /// decisive exclusion at each durable seam; the Application's early reads only give a readable refusal before that point.
    /// <list type="bullet">
    /// <item><description>While a sequence is live for a scope (its authority can advance or drains, a step preparation is reserved or
    /// unresolved, a linked operation is still Running, or a linked dispatched operation lacks coherent host Exited evidence), no
    /// attempt of its run may be inserted unless it is the exact reserved Agent operation of a step, no verification execution may
    /// be admitted for its workspace unless it is the exact reserved verification operation, and no local commit, manual checkpoint,
    /// run abandonment, verification recipe change or requested-setting/stop-policy change is admitted.</description></item>
    /// <item><description>Arming may only bind a Running run with no mutating claim and no running attempt to a coherent plan, project,
    /// workspace and checkpoint.</description></item>
    /// <item><description>The authorization, pinned recipes and step identity are immutable, an ended sequence never changes again
    /// (no rearm, resume or retarget), a step only moves Reserved to Claimed, Revoked or Unresolved and Claimed to Concluded, and the
    /// step and correction bounds hold.</description></item>
    /// </list>
    /// </summary>
    public partial class AddLocalExecutionSequence
    {
        private const string BlockedMessage = "local_execution_sequence.manual_control_blocked";
        private const string NotEligibleMessage = "local_execution_sequence.run_not_eligible";
        private const string TupleMessage = "local_execution_sequence.tuple_incoherent";
        private const string ImmutableMessage = "local_execution_sequence.immutable";
        private const string BoundMessage = "local_execution_sequence.bound_exceeded";

        private static readonly string[] TriggerNames =
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
            "trg_local_execution_guard_arming",
            "trg_local_execution_guard_tuple",
            "trg_local_execution_sequence_immutable",
            "trg_local_execution_recipe_immutable",
            "trg_local_execution_step_bounds",
            "trg_local_execution_step_immutable",
        ];

        /// <summary>The attempt shows a coherent host-recorded Exited process proof: dispatched, Exited, an exit code and a duration.</summary>
        private const string AttemptExitProof =
            "COALESCE(a.\"AgentProcessOutcome\" = 'Exited' AND a.\"AgentProcessExitCode\" IS NOT NULL "
            + "AND a.\"AgentProcessDuration\" >= 0, 0)";

        /// <summary>The verification execution shows a coherent host-recorded Exited process proof: Exited and an exit code.</summary>
        private const string ExecutionExitProof = "COALESCE(v.\"Outcome\" = 'Exited' AND v.\"ExitCode\" IS NOT NULL, 0)";

        /// <summary>
        /// The sequence is live for the given scope: its automatic authority can advance or drain, a step preparation is reserved or
        /// unresolved, a linked operation is still Running, or a linked dispatched operation lacks coherent Exited evidence (a terminal
        /// row without that proof is quarantined, never released).
        /// </summary>
        private static string Live(string scopeColumn, string scopeValue) =>
            "EXISTS (SELECT 1 FROM local_execution_sequences AS s WHERE s.\"" + scopeColumn + "\" = " + scopeValue + " AND ("
            + "s.\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping') OR EXISTS ("
            + "SELECT 1 FROM local_execution_sequence_steps AS t WHERE t.\"SequenceId\" = s.\"Id\" AND ("
            + "t.\"State\" IN ('Reserved', 'Unresolved') OR (t.\"OperationId\" IS NOT NULL AND ("
            + "(t.\"Kind\" <> 'Verification' AND EXISTS (SELECT 1 FROM attempts AS a WHERE a.\"Id\" = t.\"OperationId\" AND ("
            + "a.\"Status\" = 'Running' OR (a.\"AgentDispatchedAtUtc\" IS NOT NULL AND NOT " + AttemptExitProof + ")))) "
            + "OR (t.\"Kind\" = 'Verification' AND EXISTS (SELECT 1 FROM verification_executions AS v WHERE v.\"Id\" = t.\"OperationId\" AND ("
            + "v.\"Status\" = 'Running' OR (v.\"DispatchedAtUtc\" IS NOT NULL AND NOT " + ExecutionExitProof + "))))"
            + "))))))";

        /// <summary>The exact reserved operation of a step: its identity chosen at reservation is the only one the guard admits. A step may
        /// already read Claimed when the insert is ordered after the link inside the same save.</summary>
        private static string ReservedOperation(string kindPredicate, string scopeColumn, string scopeValue, string idValue) =>
            "EXISTS (SELECT 1 FROM local_execution_sequence_steps AS t JOIN local_execution_sequences AS s ON s.\"Id\" = t.\"SequenceId\" "
            + "WHERE t.\"ReservedOperationId\" = " + idValue + " AND t.\"State\" IN ('Reserved', 'Claimed') AND " + kindPredicate
            + " AND s.\"" + scopeColumn + "\" = " + scopeValue + ")";

        private static void CreateGuards(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_attempts BEFORE INSERT ON attempts WHEN "
                + Live("RunId", "NEW.\"RunId\"")
                + " AND NOT " + ReservedOperation("t.\"Kind\" <> 'Verification'", "RunId", "NEW.\"RunId\"", "NEW.\"Id\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_executions BEFORE INSERT ON verification_executions WHEN "
                + Live("WorkspaceId", "NEW.\"GitWorkspaceId\"")
                + " AND NOT " + ReservedOperation("t.\"Kind\" = 'Verification'", "WorkspaceId", "NEW.\"GitWorkspaceId\"", "NEW.\"Id\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_local_commit_operations BEFORE INSERT ON local_commit_operations WHEN "
                + Live("WorkspaceId", "NEW.\"GitWorkspaceId\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_run_abandonment BEFORE UPDATE OF \"Lifecycle\" ON runs WHEN "
                + "NEW.\"Lifecycle\" = 'Abandoned' AND OLD.\"Lifecycle\" <> 'Abandoned' AND "
                + Live("RunId", "NEW.\"Id\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            // A value-changing write only: the sequence's own claims compare these tokens without altering them.
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
                + Live("RunId", "NEW.\"Id\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            // A manual checkpoint is withheld; only a result checkpoint of a linked, still Running Agent operation of the workspace is
            // admitted. The result recorder's own save updates the attempt (Completed, naming this very checkpoint) before it inserts the
            // checkpoint, so the exact checkpoint the claimed linked attempt names is admitted as well.
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_checkpoints BEFORE INSERT ON git_checkpoints WHEN "
                + Live("WorkspaceId", "NEW.\"WorkspaceId\"")
                + " AND NOT EXISTS (SELECT 1 FROM local_execution_sequence_steps AS t JOIN local_execution_sequences AS s ON s.\"Id\" = t.\"SequenceId\" "
                + "JOIN attempts AS a ON a.\"Id\" = t.\"OperationId\" WHERE s.\"WorkspaceId\" = NEW.\"WorkspaceId\" AND t.\"Kind\" <> 'Verification' "
                + "AND t.\"State\" = 'Claimed' AND (a.\"Status\" = 'Running' OR a.\"AgentResultGitCheckpointId\" = NEW.\"Id\"))"
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_commands_insert BEFORE INSERT ON verification_commands WHEN "
                + Live("ProjectId", "NEW.\"ProjectId\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_commands_update BEFORE UPDATE ON verification_commands WHEN "
                + Live("ProjectId", "OLD.\"ProjectId\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_commands_delete BEFORE DELETE ON verification_commands WHEN "
                + Live("ProjectId", "OLD.\"ProjectId\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

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
                + "OR OLD.\"State\" IN ('ReadyForHumanApproval', 'HumanEscalation', 'Stopped', 'NeedsAttention', 'Interrupted') "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");

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

        private static void DropGuards(MigrationBuilder migrationBuilder)
        {
            foreach (var name in TriggerNames)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {name};");
            }
        }
    }
}
