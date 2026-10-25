using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0065: the database guards of the Human release of a terminal local operation's quarantine. The three new facts are append-only,
    /// and the nine live-fence triggers of ADR-0060/0062 are recreated with the SAME complete release predicate the Application's shared
    /// ownership definition uses (<c>LocalExecutionOwnershipRelease</c>): a dispatched linked operation that lacks ADR-0060's Exited proof
    /// stays live unless one coherent release chain exists. A receipt identifier, an observation alone or an event alone never removes it,
    /// and a malformed, foreign or duplicate chain proves nothing. As before, SQLite runs every trigger inside the writer's own write
    /// transaction, atomically with the row it guards.
    /// <para>
    /// Down refuses before any change once ANY witness, observation or release exists (a named CHECK on a temporary table aborts the
    /// migration and its transaction), so a downgrade never discards used provenance or a Human decision. With none, it removes only the
    /// new tables and restores the previous live-fence triggers.
    /// </para>
    /// </summary>
    public partial class AddLocalExecutionOwnershipRelease
    {
        private const string BlockedMessage = "local_execution_sequence.manual_control_blocked";
        private const string ImmutableMessage = "local_execution_sequence.immutable";

        /// <summary>The ADR-0060/0062 triggers whose live predicate gains the release alternative. They are recreated in both directions.</summary>
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

        private static readonly string[] AppendOnlyTables =
        [
            "local_execution_dispatch_witnesses",
            "local_execution_termination_observations",
            "local_execution_ownership_releases",
        ];

        private const string AttemptExitProof =
            "COALESCE(a.\"AgentProcessOutcome\" = 'Exited' AND a.\"AgentProcessExitCode\" IS NOT NULL "
            + "AND a.\"AgentProcessDuration\" >= 0, 0)";

        /// <summary>
        /// The frozen copy of the Agent outcomes that may accompany a dispatched attempt's timed-out or cancelled process result: every
        /// defined outcome except the clean-exit-required ones and the pre-invocation classifications of the Domain's
        /// AgentProcessEvidencePolicy at the time of this migration. The Application's LocalExecutionReleaseTerminal derives the same set
        /// from the policy, and a permanent test proves they agree.
        /// </summary>
        private const string ReleasableAgentOutcomes =
            "'SourceChanged', 'ProviderInvocationFailed', 'CheckpointEvidenceUnavailable', 'VerificationEvidenceChanged'";

        private const string ExecutionExitProof = "COALESCE(v.\"Outcome\" = 'Exited' AND v.\"ExitCode\" IS NOT NULL, 0)";

        /// <summary>The shared release-chain tail: one release names one witness, its one observation and the one step of the operation,
        /// whose sequence ended, whose admission (queued) is exactly Consumed (and absent for an Immediate cycle), and whose release event
        /// header is the Human's.</summary>
        private const string ChainJoins =
            "FROM local_execution_ownership_releases AS rl "
            + "JOIN local_execution_dispatch_witnesses AS wt ON wt.\"Id\" = rl.\"WitnessId\" "
            + "JOIN local_execution_termination_observations AS ob ON ob.\"Id\" = rl.\"ObservationId\" AND ob.\"WitnessId\" = wt.\"Id\" "
            + "JOIN local_execution_sequence_steps AS rt ON rt.\"Id\" = wt.\"StepId\" AND rt.\"Id\" = rl.\"StepId\" "
            + "JOIN local_execution_sequences AS rs ON rs.\"Id\" = rt.\"SequenceId\" "
            + "JOIN events AS ev ON ev.\"Id\" = rl.\"EventId\" ";

        /// <summary>
        /// The identity, source, mode and chronology half of the proof, identical to the Application's LocalExecutionReleaseChain: the
        /// witness equals the step and sequence tuple, the run is a ManualAgent run of the sequence's project, the workspace and the source
        /// checkpoint (workspace and fingerprint) are the step's own, the recipe and step counts are sound, the observation never outlasts
        /// its process and is recorded no earlier than the dispatch, and the receipt is recorded no earlier than the observation and
        /// expires after it. Stored times are normalized UTC text, so their text order is their chronological order.
        /// </summary>
        private const string ChainCommon =
            "wt.\"SequenceId\" = rs.\"Id\" AND wt.\"RunId\" = rs.\"RunId\" AND wt.\"ProjectId\" = rs.\"ProjectId\" "
            + "AND wt.\"WorkspaceId\" = rs.\"WorkspaceId\" AND wt.\"Kind\" = rt.\"Kind\" AND wt.\"Ordinal\" = rt.\"Ordinal\" "
            + "AND wt.\"PassNumber\" = rt.\"PassNumber\" AND wt.\"RecipeOrdinal\" IS rt.\"RecipeOrdinal\" "
            + "AND wt.\"PredecessorStepId\" IS rt.\"PredecessorStepId\" AND wt.\"SourceCheckpointId\" = rt.\"SourceCheckpointId\" "
            + "AND wt.\"SourceFingerprintSha256\" = rt.\"SourceFingerprintSha256\" AND wt.\"ReportMessageId\" IS rt.\"ReportMessageId\" "
            + "AND wt.\"SubjectAttemptId\" IS rt.\"SubjectAttemptId\" AND rt.\"State\" IN ('Claimed', 'Concluded') "
            + "AND rs.\"State\" IN ('Stopped', 'NeedsAttention', 'Interrupted') "
            + "AND EXISTS (SELECT 1 FROM runs AS rn WHERE rn.\"Id\" = rs.\"RunId\" AND rn.\"ProjectId\" = rs.\"ProjectId\" "
            + "AND typeof(rn.\"ExecutionMode\") = 'integer' AND rn.\"ExecutionMode\" = 2) "
            + "AND EXISTS (SELECT 1 FROM git_workspaces AS gw WHERE gw.\"Id\" = rs.\"WorkspaceId\" AND gw.\"ProjectId\" = rs.\"ProjectId\") "
            + "AND EXISTS (SELECT 1 FROM git_checkpoints AS gc WHERE gc.\"Id\" = rt.\"SourceCheckpointId\" "
            + "AND gc.\"WorkspaceId\" = rs.\"WorkspaceId\" AND gc.\"FingerprintSha256\" = rt.\"SourceFingerprintSha256\") "
            + "AND (SELECT COUNT(*) FROM local_execution_sequence_recipes AS rr WHERE rr.\"SequenceId\" = rs.\"Id\") = rs.\"RecipeCount\" "
            + "AND (SELECT COUNT(*) FROM local_execution_sequence_steps AS rp WHERE rp.\"SequenceId\" = rs.\"Id\") <= 3 * rs.\"RecipeCount\" + 6 "
            + "AND ob.\"ObservedElapsed\" <= ob.\"ProcessDuration\" AND ob.\"RecordedAtUtc\" >= wt.\"DispatchedAtUtc\" "
            + "AND rl.\"ConfirmationVersion\" = 1 AND rl.\"ReleasedAtUtc\" >= ob.\"RecordedAtUtc\" "
            + "AND rl.\"ConfirmationExpiresAtUtc\" > rl.\"ReleasedAtUtc\" "
            + "AND ((rs.\"AdmissionMode\" = 'Immediate' AND wt.\"AdmissionId\" IS NULL "
            + "AND NOT EXISTS (SELECT 1 FROM local_execution_admissions AS ax WHERE ax.\"StepId\" = rt.\"Id\")) "
            + "OR (rs.\"AdmissionMode\" = 'WaitForAdmission' "
            + "AND EXISTS (SELECT 1 FROM local_execution_admissions AS ad WHERE ad.\"Id\" = wt.\"AdmissionId\" AND ad.\"StepId\" = rt.\"Id\" "
            + "AND ad.\"SequenceId\" = rs.\"Id\" AND ad.\"State\" = 'Consumed' AND ad.\"Ordinal\" = rt.\"Ordinal\" AND ad.\"Kind\" = rt.\"Kind\" "
            + "AND ad.\"PassNumber\" = rt.\"PassNumber\" AND ad.\"RecipeOrdinal\" IS rt.\"RecipeOrdinal\" "
            + "AND ad.\"SourceCheckpointId\" = rt.\"SourceCheckpointId\"))) "
            + "AND rl.\"IntentVersion\" = 1 AND rl.\"IntentConfirmationVersion\" = rl.\"ConfirmationVersion\" AND rl.\"IntentConfirmationExpiresAtUtc\" = rl.\"ConfirmationExpiresAtUtc\" "
            + "AND rl.\"IntentSequenceId\" = rs.\"Id\" AND rl.\"IntentRunId\" = rs.\"RunId\" AND rl.\"IntentProjectId\" = rs.\"ProjectId\" AND rl.\"IntentWorkspaceId\" = rs.\"WorkspaceId\" "
            + "AND rl.\"IntentSequenceState\" = rs.\"State\" AND rl.\"IntentAdmissionMode\" = rs.\"AdmissionMode\" AND rl.\"IntentAdmissionId\" IS wt.\"AdmissionId\" "
            + "AND rl.\"IntentKind\" = rt.\"Kind\" AND rl.\"IntentOrdinal\" = rt.\"Ordinal\" AND rl.\"IntentPassNumber\" = rt.\"PassNumber\" "
            + "AND rl.\"IntentRecipeOrdinal\" IS rt.\"RecipeOrdinal\" AND rl.\"IntentPredecessorStepId\" IS rt.\"PredecessorStepId\" "
            + "AND rl.\"IntentReportMessageId\" IS rt.\"ReportMessageId\" AND rl.\"IntentSubjectAttemptId\" IS rt.\"SubjectAttemptId\" "
            + "AND rl.\"IntentSourceCheckpointId\" = rt.\"SourceCheckpointId\" AND rl.\"IntentSourceFingerprintSha256\" = rt.\"SourceFingerprintSha256\" "
            + "AND rl.\"IntentProcessOutcome\" = ob.\"ProcessOutcome\" AND rl.\"IntentSemanticExitCode\" IS ob.\"SemanticExitCode\" "
            + "AND rl.\"IntentProcessDuration\" = ob.\"ProcessDuration\" AND rl.\"IntentPhysicalExitCode\" = ob.\"PhysicalExitCode\" "
            + "AND rl.\"IntentObservedElapsed\" = ob.\"ObservedElapsed\" AND rl.\"IntentObservationRecordedAtUtc\" = ob.\"RecordedAtUtc\" "
            + "AND ev.\"RunId\" = wt.\"RunId\" AND ev.\"EventType\" = 'local_execution_sequence.ownership_released' "
            + "AND ev.\"ActorKind\" = 'Human'";

        /// <summary>One coherent release chain of the Agent attempt <c>a</c>: the exact attempt, run, workspace, source, dispatch time,
        /// contract and terminal process tuple the observation was recorded for.</summary>
        private const string AgentReleased =
            "EXISTS (SELECT 1 " + ChainJoins
            + "WHERE rt.\"OperationId\" = a.\"Id\" AND wt.\"OperationFamily\" = 'Agent' AND wt.\"AgentAttemptId\" = a.\"Id\" "
            + "AND wt.\"VerificationExecutionId\" IS NULL AND " + ChainCommon + " "
            + "AND a.\"Status\" = 'Failed' AND a.\"AgentOutcome\" IN (" + ReleasableAgentOutcomes + ") "
            + "AND a.\"AgentProcessOutcome\" IN ('TimedOut', 'Cancelled') AND a.\"AgentProcessExitCode\" IS NULL "
            + "AND a.\"AgentProcessDuration\" >= 0 AND a.\"AgentCheckpointFingerprintSha256\" = rt.\"SourceFingerprintSha256\" "
            + "AND NOT EXISTS (SELECT 1 FROM verification_executions AS vx WHERE vx.\"Id\" = a.\"Id\") "
            + "AND a.\"RunId\" = rs.\"RunId\" "
            + "AND a.\"AgentGitWorkspaceId\" = rs.\"WorkspaceId\" AND a.\"AgentGitCheckpointId\" = rt.\"SourceCheckpointId\" "
            + "AND a.\"AgentDispatchedAtUtc\" = wt.\"DispatchedAtUtc\" AND a.\"AgentResponseContract\" = wt.\"AgentResponseContract\" "
            + "AND CASE rt.\"Kind\" WHEN 'Implementation' THEN a.\"AgentResponseContract\" = 'ImplementationReport' "
            + "WHEN 'CodeReview' THEN a.\"AgentResponseContract\" = 'ImplementationReview' "
            + "WHEN 'VerificationDiagnosis' THEN a.\"AgentResponseContract\" = 'VerificationDiagnosis' "
            + "WHEN 'ReviewCorrection' THEN a.\"AgentResponseContract\" = 'ReviewCorrection' "
            + "WHEN 'DiagnosisCorrection' THEN a.\"AgentResponseContract\" = 'ReviewCorrection' ELSE 0 END "
            + "AND a.\"AgentProcessOutcome\" = ob.\"ProcessOutcome\" AND a.\"AgentProcessExitCode\" IS ob.\"SemanticExitCode\" "
            + "AND rl.\"IntentFamily\" = 'Agent' AND rl.\"IntentOperationId\" = a.\"Id\" AND rl.\"IntentOperationStatus\" = 'Failed' "
            + "AND rl.\"IntentAgentOutcome\" IS a.\"AgentOutcome\" AND rl.\"IntentAgentResponseContract\" IS a.\"AgentResponseContract\" "
            + "AND rl.\"IntentCompletionFingerprintSha256\" IS NULL AND rl.\"IntentRecipeCommandId\" IS NULL AND rl.\"IntentRunId\" = a.\"RunId\" "
            + "AND rl.\"IntentWorkspaceId\" = a.\"AgentGitWorkspaceId\" AND rl.\"IntentSourceCheckpointId\" = a.\"AgentGitCheckpointId\" "
            + "AND rl.\"IntentSourceFingerprintSha256\" = a.\"AgentCheckpointFingerprintSha256\" AND rl.\"IntentDispatchedAtUtc\" = a.\"AgentDispatchedAtUtc\" "
            + "AND rl.\"IntentProcessOutcome\" = a.\"AgentProcessOutcome\" AND rl.\"IntentSemanticExitCode\" IS a.\"AgentProcessExitCode\" "
            + "AND rl.\"IntentProcessDuration\" = a.\"AgentProcessDuration\" "
            + "AND a.\"AgentProcessDuration\" = ob.\"ProcessDuration\")";

        /// <summary>One coherent release chain of the verification execution <c>v</c>.</summary>
        private const string ExecutionReleased =
            "EXISTS (SELECT 1 " + ChainJoins
            + "WHERE rt.\"OperationId\" = v.\"Id\" AND wt.\"OperationFamily\" = 'Verification' AND wt.\"VerificationExecutionId\" = v.\"Id\" "
            + "AND wt.\"AgentAttemptId\" IS NULL AND rt.\"Kind\" = 'Verification' AND " + ChainCommon + " "
            + "AND v.\"Outcome\" IN ('TimedOut', 'Cancelled') AND v.\"ExitCode\" IS NULL AND v.\"CompletionFingerprintSha256\" IS NOT NULL "
            + "AND ((v.\"Status\" = 'TimedOut' AND v.\"Outcome\" = 'TimedOut' "
            + "AND v.\"CompletionFingerprintSha256\" = v.\"CheckpointFingerprintSha256\") "
            + "OR (v.\"Status\" = 'Cancelled' AND v.\"Outcome\" = 'Cancelled' "
            + "AND v.\"CompletionFingerprintSha256\" = v.\"CheckpointFingerprintSha256\") "
            + "OR (v.\"Status\" = 'SourceChanged' AND v.\"CompletionFingerprintSha256\" <> v.\"CheckpointFingerprintSha256\")) "
            + "AND NOT EXISTS (SELECT 1 FROM attempts AS ax WHERE ax.\"Id\" = v.\"Id\") AND v.\"ProjectId\" = rs.\"ProjectId\" "
            + "AND v.\"GitWorkspaceId\" = rs.\"WorkspaceId\" AND v.\"GitCheckpointId\" = rt.\"SourceCheckpointId\" "
            + "AND v.\"CheckpointFingerprintSha256\" = rt.\"SourceFingerprintSha256\" AND v.\"DispatchedAtUtc\" = wt.\"DispatchedAtUtc\" "
            + "AND v.\"VerificationCommandId\" = wt.\"RecipeCommandId\" "
            + "AND EXISTS (SELECT 1 FROM local_execution_sequence_recipes AS rc WHERE rc.\"SequenceId\" = rs.\"Id\" "
            + "AND rc.\"Ordinal\" = rt.\"RecipeOrdinal\" AND rc.\"VerificationCommandId\" = wt.\"RecipeCommandId\") "
            + "AND v.\"Outcome\" = ob.\"ProcessOutcome\" AND v.\"ExitCode\" IS ob.\"SemanticExitCode\" "
            + "AND rl.\"IntentFamily\" = 'Verification' AND rl.\"IntentOperationId\" = v.\"Id\" AND rl.\"IntentOperationStatus\" = v.\"Status\" "
            + "AND rl.\"IntentAgentOutcome\" IS NULL AND rl.\"IntentAgentResponseContract\" IS NULL "
            + "AND rl.\"IntentCompletionFingerprintSha256\" IS v.\"CompletionFingerprintSha256\" AND rl.\"IntentRecipeCommandId\" = v.\"VerificationCommandId\" "
            + "AND rl.\"IntentProjectId\" = v.\"ProjectId\" AND rl.\"IntentWorkspaceId\" = v.\"GitWorkspaceId\" "
            + "AND rl.\"IntentSourceCheckpointId\" = v.\"GitCheckpointId\" AND rl.\"IntentSourceFingerprintSha256\" = v.\"CheckpointFingerprintSha256\" "
            + "AND rl.\"IntentDispatchedAtUtc\" = v.\"DispatchedAtUtc\" AND rl.\"IntentSemanticExitCode\" IS v.\"ExitCode\" AND rl.\"IntentProcessOutcome\" = v.\"Outcome\")";

        /// <summary>
        /// The sequence is live for the given scope (ADR-0060/0062): its automatic authority can advance, drain or wait, a step preparation
        /// is reserved or unresolved, a linked operation is still Running, or a linked dispatched operation lacks coherent Exited evidence.
        /// <paramref name="withRelease"/> adds ADR-0065's only alternative: unless one coherent Human release chain exists for it.
        /// </summary>
        private static string Live(string scopeColumn, string scopeValue, bool withRelease, bool includeWaiting = true) =>
            "EXISTS (SELECT 1 FROM local_execution_sequences AS s WHERE s.\"" + scopeColumn + "\" = " + scopeValue + " AND ("
            + (includeWaiting
                ? "s.\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'WaitingForAdmission')"
                : "s.\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping')")
            + " OR EXISTS ("
            + "SELECT 1 FROM local_execution_sequence_steps AS t WHERE t.\"SequenceId\" = s.\"Id\" AND ("
            + "t.\"State\" IN ('Reserved', 'Unresolved') OR (t.\"OperationId\" IS NOT NULL AND ("
            + "(t.\"Kind\" <> 'Verification' AND EXISTS (SELECT 1 FROM attempts AS a WHERE a.\"Id\" = t.\"OperationId\" AND ("
            + "a.\"Status\" = 'Running' OR (a.\"AgentDispatchedAtUtc\" IS NOT NULL AND NOT " + AttemptExitProof
            + (withRelease ? " AND NOT " + AgentReleased : string.Empty) + ")))) "
            + "OR (t.\"Kind\" = 'Verification' AND EXISTS (SELECT 1 FROM verification_executions AS v WHERE v.\"Id\" = t.\"OperationId\" AND ("
            + "v.\"Status\" = 'Running' OR (v.\"DispatchedAtUtc\" IS NOT NULL AND NOT " + ExecutionExitProof
            + (withRelease ? " AND NOT " + ExecutionReleased : string.Empty) + "))))"
            + "))))))";

        private static string ReservedOperation(string kindPredicate, string scopeColumn, string scopeValue, string idValue) =>
            "EXISTS (SELECT 1 FROM local_execution_sequence_steps AS t JOIN local_execution_sequences AS s ON s.\"Id\" = t.\"SequenceId\" "
            + "WHERE t.\"ReservedOperationId\" = " + idValue + " AND t.\"State\" IN ('Reserved', 'Claimed') AND " + kindPredicate
            + " AND s.\"" + scopeColumn + "\" = " + scopeValue + ")";

        /// <summary>Up: the append-only guards of the three new facts, then the live-fence triggers recreated with the release predicate.</summary>
        private static void CreateGuards(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AppendOnlyTables)
            {
                migrationBuilder.Sql(
                    $"CREATE TRIGGER trg_{table}_no_update BEFORE UPDATE ON {table} "
                    + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");
                migrationBuilder.Sql(
                    $"CREATE TRIGGER trg_{table}_no_delete BEFORE DELETE ON {table} "
                    + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");
            }

            ReplaceLiveGuards(migrationBuilder, withRelease: true);
        }

        /// <summary>Down, after the refusal check: the new facts' guards removed, the previous live-fence triggers restored.</summary>
        private static void DropGuards(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AppendOnlyTables)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{table}_no_update;");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{table}_no_delete;");
            }

            ReplaceLiveGuards(migrationBuilder, withRelease: false);
        }

        private static void ReplaceLiveGuards(MigrationBuilder migrationBuilder, bool withRelease)
        {
            foreach (var name in LiveTriggerNames)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {name};");
            }

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_attempts BEFORE INSERT ON attempts WHEN "
                + Live("RunId", "NEW.\"RunId\"", withRelease)
                + " AND NOT " + ReservedOperation("t.\"Kind\" <> 'Verification'", "RunId", "NEW.\"RunId\"", "NEW.\"Id\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_executions BEFORE INSERT ON verification_executions WHEN "
                + Live("WorkspaceId", "NEW.\"GitWorkspaceId\"", withRelease)
                + " AND NOT " + ReservedOperation("t.\"Kind\" = 'Verification'", "WorkspaceId", "NEW.\"GitWorkspaceId\"", "NEW.\"Id\"")
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_local_commit_operations BEFORE INSERT ON local_commit_operations WHEN "
                + Live("WorkspaceId", "NEW.\"GitWorkspaceId\"", withRelease)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_run_abandonment BEFORE UPDATE OF \"Lifecycle\" ON runs WHEN "
                + "NEW.\"Lifecycle\" = 'Abandoned' AND OLD.\"Lifecycle\" <> 'Abandoned' AND "
                + Live("RunId", "NEW.\"Id\"", withRelease)
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
                + Live("RunId", "NEW.\"Id\"", withRelease)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            // A manual checkpoint is withheld; only a result checkpoint of a linked, still Running Agent operation of the workspace is
            // admitted. ADR-0062: a workspace that is only waited for owns no execution, so the result checkpoint an Agent attempt of
            // that workspace names as its own (another run's already executing attempt) is admitted as well; a capture is not.
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_checkpoints BEFORE INSERT ON git_checkpoints WHEN "
                + Live("WorkspaceId", "NEW.\"WorkspaceId\"", withRelease)
                + " AND NOT EXISTS (SELECT 1 FROM local_execution_sequence_steps AS t JOIN local_execution_sequences AS s ON s.\"Id\" = t.\"SequenceId\" "
                + "JOIN attempts AS a ON a.\"Id\" = t.\"OperationId\" WHERE s.\"WorkspaceId\" = NEW.\"WorkspaceId\" AND t.\"Kind\" <> 'Verification' "
                + "AND t.\"State\" = 'Claimed' AND (a.\"Status\" = 'Running' OR a.\"AgentResultGitCheckpointId\" = NEW.\"Id\"))"
                + " AND NOT (NOT " + Live("WorkspaceId", "NEW.\"WorkspaceId\"", withRelease, includeWaiting: false)
                + " AND EXISTS (SELECT 1 FROM attempts AS r WHERE r.\"AgentGitWorkspaceId\" = NEW.\"WorkspaceId\" "
                + "AND r.\"AgentResultGitCheckpointId\" = NEW.\"Id\"))"
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_commands_insert BEFORE INSERT ON verification_commands WHEN "
                + Live("ProjectId", "NEW.\"ProjectId\"", withRelease)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_commands_update BEFORE UPDATE ON verification_commands WHEN "
                + Live("ProjectId", "OLD.\"ProjectId\"", withRelease)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");

            migrationBuilder.Sql(
                "CREATE TRIGGER trg_local_execution_guard_verification_commands_delete BEFORE DELETE ON verification_commands WHEN "
                + Live("ProjectId", "OLD.\"ProjectId\"", withRelease)
                + $" BEGIN SELECT RAISE(ABORT, '{BlockedMessage}'); END;");
        }
    }
}
