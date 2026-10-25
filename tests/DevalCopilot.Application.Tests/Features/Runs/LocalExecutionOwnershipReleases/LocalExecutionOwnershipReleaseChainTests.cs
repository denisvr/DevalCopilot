using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Ids = DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases.LocalExecutionOwnershipReleaseTamper.Ids;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065: the release proof is ONE complete coherent chain, decided identically by the protected read, the Application's shared
/// ownership predicates and the database fence triggers. Starting from a real Released operation, each case changes exactly one stored
/// fact of the chain (witness, observation, release, event header, operation row, step, sequence, admission or recipe) through a direct
/// guarded write; the read then refuses to show a release, the workspace, capacity and abandonment readings never count the operation
/// as free, and the write-seam probe of the live-fence guards is still refused. A receipt identifier, an observation alone or an event
/// alone never subtracts an owner.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseChainTests : IAsyncLifetime
{
    private static readonly string OtherFingerprint = new('9', 64);
    private const string EarlierTime = "2000-01-01 00:00:00+00:00";

    private sealed record Tamper(
        Step Kind,
        bool Queued,
        string Table,
        Func<Ids, (string Sql, object[] Parameters)>? Build,
        Func<SqliteDatabaseFixture, Ids, Task>? Custom = null,
        Func<SqliteDatabaseFixture, LocalExecutionSequenceStep, Task>? Prepare = null);

    private static Tamper Custom(Step kind, bool queued, Func<SqliteDatabaseFixture, Ids, Task> change) =>
        new(kind, queued, string.Empty, null, change);

    /// <summary>A legitimate SourceChanged verification: its completion fingerprint is a valid SHA-256 that differs from the pinned source's.</summary>
    private static Task PrepareSourceChangedVerificationAsync(SqliteDatabaseFixture database, LocalExecutionSequenceStep target) =>
        LocalExecutionOwnershipReleaseTamper.ApplyAsync(
            database, "verification_executions",
            "UPDATE verification_executions SET \"Status\" = 'SourceChanged', \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}",
            new string('9', 64), target.OperationId!.Value);

    /// <summary>The project a run is moved to by the run-project cases, so abandonment can be asked in the run's actual scope.</summary>
    private static readonly Guid MovedProjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly Dictionary<string, Tamper> Catalog = new()
    {
        // ---- the dispatch witness --------------------------------------------------------------------------------------------------
        ["witness-fingerprint"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"SourceFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [OtherFingerprint, ids.WitnessId])),
        ["witness-source-checkpoint"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"SourceCheckpointId\" = {0} WHERE \"Id\" = {1}", [ids.OtherCheckpointId, ids.WitnessId])),
        ["witness-run"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"RunId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.WitnessId])),
        ["witness-project"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"ProjectId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.WitnessId])),
        ["witness-workspace"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"WorkspaceId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.WitnessId])),
        ["witness-sequence"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"SequenceId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.WitnessId])),
        ["witness-step"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"StepId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.WitnessId])),
        ["witness-dispatch-time"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"DispatchedAtUtc\" = {0} WHERE \"Id\" = {1}", [EarlierTime, ids.WitnessId])),
        ["witness-contract"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"AgentResponseContract\" = 'ReviewCorrection' WHERE \"Id\" = {0}", [ids.WitnessId])),
        ["witness-admission-present-on-immediate"] = new(Step.CodeReview, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"AdmissionId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.WitnessId])),
        ["witness-admission-absent-on-queued"] = new(Step.Implementation, true, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"AdmissionId\" = NULL WHERE \"Id\" = {0}", [ids.WitnessId])),
        ["witness-recipe-on-verification"] = new(Step.Verification, false, "local_execution_dispatch_witnesses",
            ids => ("UPDATE local_execution_dispatch_witnesses SET \"RecipeCommandId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.WitnessId])),

        // ---- the physical termination observation ------------------------------------------------------------------------------------
        ["observation-outcome"] = new(Step.CodeReview, false, "local_execution_termination_observations",
            ids => ("UPDATE local_execution_termination_observations SET \"ProcessOutcome\" = 'Cancelled' WHERE \"Id\" = {0}", [ids.ObservationId])),
        ["observation-duration"] = new(Step.CodeReview, false, "local_execution_termination_observations",
            ids => ("UPDATE local_execution_termination_observations SET \"ProcessDuration\" = \"ProcessDuration\" + 1 WHERE \"Id\" = {0}", [ids.ObservationId])),
        ["observation-witness"] = new(Step.CodeReview, false, "local_execution_termination_observations",
            ids => ("UPDATE local_execution_termination_observations SET \"WitnessId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.ObservationId])),
        ["observation-outcome-on-verification"] = new(Step.Verification, false, "local_execution_termination_observations",
            ids => ("UPDATE local_execution_termination_observations SET \"ProcessOutcome\" = 'Cancelled' WHERE \"Id\" = {0}", [ids.ObservationId])),

        // ---- the release and its Human event -----------------------------------------------------------------------------------------
        ["release-witness"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"WitnessId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.ReleaseId])),
        ["release-observation"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"ObservationId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.ReleaseId])),
        ["release-step"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"StepId\" = {0} WHERE \"Id\" = {1}", [ids.OtherStepId, ids.ReleaseId])),
        ["release-event-missing"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"EventId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.ReleaseId])),
        ["release-event-of-another-type"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"EventId\" = (SELECT \"Id\" FROM events WHERE \"EventType\" = 'local_execution_sequence.armed' LIMIT 1) "
                + "WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["event-type"] = new(Step.CodeReview, false, "events",
            ids => ("UPDATE events SET \"EventType\" = 'run.started' WHERE \"Id\" = {0}", [ids.EventId])),
        ["event-actor"] = new(Step.CodeReview, false, "events",
            ids => ("UPDATE events SET \"ActorKind\" = 'Orchestrator' WHERE \"Id\" = {0}", [ids.EventId])),
        ["event-run"] = new(Step.CodeReview, false, "events",
            ids => ("UPDATE events SET \"RunId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.EventId])),

        // ---- the Agent operation row ----------------------------------------------------------------------------------------------------
        ["attempt-running"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"Status\" = 'Running' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-dispatch-time"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentDispatchedAtUtc\" = {0} WHERE \"Id\" = {1}", [EarlierTime, ids.OperationId])),
        ["attempt-process-outcome"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentProcessOutcome\" = 'Cancelled' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-process-duration"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentProcessDuration\" = \"AgentProcessDuration\" + 1 WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-workspace"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentGitWorkspaceId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.OperationId])),
        ["attempt-checkpoint"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentGitCheckpointId\" = {0} WHERE \"Id\" = {1}", [ids.OtherCheckpointId, ids.OperationId])),
        ["attempt-contract"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentResponseContract\" = 'ReviewCorrection' WHERE \"Id\" = {0}", [ids.OperationId])),

        // ---- the verification operation row and its pinned recipe ---------------------------------------------------------------------
        ["execution-running"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"Status\" = 'Running' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["execution-outcome"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"Outcome\" = 'Cancelled' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["execution-dispatch-time"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"DispatchedAtUtc\" = {0} WHERE \"Id\" = {1}", [EarlierTime, ids.OperationId])),
        ["execution-fingerprint"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"CheckpointFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [OtherFingerprint, ids.OperationId])),
        ["execution-workspace"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"GitWorkspaceId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.OperationId])),
        ["execution-command"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"VerificationCommandId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.OperationId])),
        // ---- ADR-0065 correction LQR-R1/LQR-R2: the complete historical identity, source, mode, chronology and terminal consistency ------
        ["source-checkpoint-fingerprint"] = new(Step.CodeReview, false, "git_checkpoints",
            ids => ("UPDATE git_checkpoints SET \"FingerprintSha256\" = {0} WHERE \"Id\" = {1}", [OtherFingerprint, ids.SourceCheckpointId])),
        ["source-checkpoint-fingerprint-on-verification"] = new(Step.Verification, false, "git_checkpoints",
            ids => ("UPDATE git_checkpoints SET \"FingerprintSha256\" = {0} WHERE \"Id\" = {1}", [OtherFingerprint, ids.SourceCheckpointId])),
        ["source-checkpoint-workspace"] = new(Step.CodeReview, false, "git_checkpoints",
            ids => ("UPDATE git_checkpoints SET \"WorkspaceId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.SourceCheckpointId])),
        ["workspace-project"] = new(Step.CodeReview, false, "git_workspaces",
            ids => ("UPDATE git_workspaces SET \"ProjectId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.WorkspaceId])),
        ["run-project"] = new(Step.CodeReview, false, "runs",
            ids => ("UPDATE runs SET \"ProjectId\" = {0} WHERE \"Id\" = {1}", [MovedProjectId, ids.RunId])),
        ["run-project-on-verification"] = new(Step.Verification, false, "runs",
            ids => ("UPDATE runs SET \"ProjectId\" = {0} WHERE \"Id\" = {1}", [MovedProjectId, ids.RunId])),
        ["run-simulated"] = new(Step.CodeReview, false, "runs",
            ids => ("UPDATE runs SET \"ExecutionMode\" = 1 WHERE \"Id\" = {0}", [ids.RunId])),
        ["run-simulated-on-verification"] = new(Step.Verification, false, "runs",
            ids => ("UPDATE runs SET \"ExecutionMode\" = 1 WHERE \"Id\" = {0}", [ids.RunId])),
        ["run-legacy-on-queued"] = new(Step.Implementation, true, "runs",
            ids => ("UPDATE runs SET \"ExecutionMode\" = 0 WHERE \"Id\" = {0}", [ids.RunId])),
        ["observation-before-dispatch"] = new(Step.CodeReview, false, "local_execution_termination_observations",
            ids => ("UPDATE local_execution_termination_observations SET \"RecordedAtUtc\" = {0} WHERE \"Id\" = {1}", [EarlierTime, ids.ObservationId])),
        ["observation-before-dispatch-on-verification"] = new(Step.Verification, false, "local_execution_termination_observations",
            ids => ("UPDATE local_execution_termination_observations SET \"RecordedAtUtc\" = {0} WHERE \"Id\" = {1}", [EarlierTime, ids.ObservationId])),
        ["release-expiry-before-release"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"ConfirmationExpiresAtUtc\" = {0} WHERE \"Id\" = {1}", [EarlierTime, ids.ReleaseId])),
        ["release-expiry-before-release-on-verification"] = new(Step.Verification, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"ConfirmationExpiresAtUtc\" = {0} WHERE \"Id\" = {1}", [EarlierTime, ids.ReleaseId])),
        ["release-before-observation"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"ReleasedAtUtc\" = {0}, \"ConfirmationExpiresAtUtc\" = {1} WHERE \"Id\" = {2}",
                [EarlierTime, "2000-01-01 00:01:00+00:00", ids.ReleaseId])),
        ["attempt-status-completed-with-timeout"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"Status\" = 'Completed' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-status-interrupted-with-observation"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"Status\" = 'Interrupted' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-outcome-unknown"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentOutcome\" = 'Unrecognized' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-outcome-absent"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentOutcome\" = NULL WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-clean-exit-outcome-with-timeout"] = new(Step.Implementation, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentOutcome\" = 'Implemented' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-clean-exit-outcome-with-timeout-on-review"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentOutcome\" = 'ReviewApproved' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-pre-invocation-outcome"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentOutcome\" = 'InputAlreadyCodeReviewed' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["attempt-checkpoint-fingerprint"] = new(Step.Implementation, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentCheckpointFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [OtherFingerprint, ids.OperationId])),
        ["attempt-checkpoint-fingerprint-on-review"] = new(Step.CodeReview, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentCheckpointFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [OtherFingerprint, ids.OperationId])),
        ["execution-status-failed-with-timeout"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"Status\" = 'Failed' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["execution-status-cancelled-with-timeout"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"Status\" = 'Cancelled' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["execution-source-changed-with-matching-completion"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"Status\" = 'SourceChanged' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["execution-completion-fingerprint-differs-from-timeout"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [OtherFingerprint, ids.OperationId])),
        ["execution-completion-fingerprint-absent"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"CompletionFingerprintSha256\" = NULL WHERE \"Id\" = {0}", [ids.OperationId])),
        ["execution-exit-code-with-timeout"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"ExitCode\" = 0 WHERE \"Id\" = {0}", [ids.OperationId])),
        ["execution-outcome-absent"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"Outcome\" = NULL WHERE \"Id\" = {0}", [ids.OperationId])),
        ["admission-present-on-an-immediate-cycle"] = Custom(Step.Implementation, true, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "local_execution_dispatch_witnesses", "UPDATE local_execution_dispatch_witnesses SET \"AdmissionId\" = NULL WHERE \"Id\" = {0}", ids.WitnessId);
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "local_execution_sequences", "UPDATE local_execution_sequences SET \"AdmissionMode\" = 'Immediate' WHERE \"Id\" = {0}", ids.SequenceId);
        }),
        ["attempt-identity-collides-with-a-verification-execution"] = Custom(Step.CodeReview, false, async (database, ids) =>
        {
            var execution = await ReadFirstAsync(database, "verification_executions");
            await LocalExecutionOwnershipReleaseTamper.CloneRowAsync(
                database, "verification_executions", execution, new Dictionary<string, object> { ["Id"] = ids.OperationId, ["ExecutionNumber"] = 9999 });
        }),
        ["execution-identity-collides-with-an-agent-attempt"] = Custom(Step.Verification, false, async (database, ids) =>
        {
            var attempt = await ReadFirstAsync(database, "attempts");
            await LocalExecutionOwnershipReleaseTamper.CloneRowAsync(
                database, "attempts", attempt, new Dictionary<string, object> { ["Id"] = ids.OperationId, ["AttemptNumber"] = 9999, ["AgentBudgetSlot"] = 9999 });
        }),
        // ---- ADR-0066: every column of the typed intent snapshot drifts independently of the facts it recorded -------------------------
        ["intent-operation-id"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentOperationId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-sequence-id"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentSequenceId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-run-id"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentRunId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-project-id"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentProjectId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-workspace-id"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentWorkspaceId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-sequence-state"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentSequenceState\" = 'Stopped' WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-admission-mode-and-id-on-an-immediate-cycle"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentAdmissionMode\" = 'WaitForAdmission', \"IntentAdmissionId\" = {1} WHERE \"Id\" = {0}",
                [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-admission-id"] = new(Step.Implementation, true, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentAdmissionId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-kind"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentKind\" = 'VerificationDiagnosis' WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-ordinal"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentOrdinal\" = \"IntentOrdinal\" + 1 WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-pass-number"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentPassNumber\" = \"IntentPassNumber\" + 1 WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-recipe-ordinal"] = new(Step.Verification, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentRecipeOrdinal\" = \"IntentRecipeOrdinal\" + 1 WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-predecessor-step-id"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentPredecessorStepId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-source-checkpoint-id"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentSourceCheckpointId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-source-fingerprint-sha256"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentSourceFingerprintSha256\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, OtherFingerprint])),
        ["intent-report-message-id"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentReportMessageId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-subject-attempt-id"] = new(Step.ReviewCorrection, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentSubjectAttemptId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-agent-response-contract"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentAgentResponseContract\" = 'ReviewCorrection' WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-recipe-command-id"] = new(Step.Verification, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentRecipeCommandId\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, Guid.NewGuid()])),
        ["intent-operation-status"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentOperationStatus\" = 'Interrupted' WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-operation-status-on-verification"] = new(Step.Verification, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentOperationStatus\" = 'Cancelled' WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-agent-outcome"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentAgentOutcome\" = 'SourceChanged' WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-completion-fingerprint-sha256"] = new(Step.Verification, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentCompletionFingerprintSha256\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, OtherFingerprint])),
        ["intent-process-outcome"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentProcessOutcome\" = 'Cancelled' WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-process-duration"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentProcessDuration\" = \"IntentProcessDuration\" + 1 WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-physical-exit-code"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentPhysicalExitCode\" = \"IntentPhysicalExitCode\" + 1 WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-observed-elapsed"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentObservedElapsed\" = \"IntentObservedElapsed\" - 1 WHERE \"Id\" = {0}", [ids.ReleaseId])),
        ["intent-dispatched-at-utc"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentDispatchedAtUtc\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, EarlierTime])),
        ["intent-observation-recorded-at-utc"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentObservationRecordedAtUtc\" = {1} WHERE \"Id\" = {0}", [ids.ReleaseId, EarlierTime])),
        ["intent-confirmation-expires-at-utc"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"IntentConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"IntentConfirmationExpiresAtUtc\", '-1 seconds') || '+00:00' WHERE \"Id\" = {0}",
                [ids.ReleaseId])),
        ["confirmation-expires-at-utc-alone-within-the-lifetime"] = new(Step.CodeReview, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"ConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"ConfirmationExpiresAtUtc\", '-1 seconds') || '+00:00' WHERE \"Id\" = {0}",
                [ids.ReleaseId])),
        ["confirmation-expires-at-utc-alone-on-verification"] = new(Step.Verification, false, "local_execution_ownership_releases",
            ids => ("UPDATE local_execution_ownership_releases SET \"ConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"ConfirmationExpiresAtUtc\", '-1 seconds') || '+00:00' WHERE \"Id\" = {0}",
                [ids.ReleaseId])),

        // ---- ADR-0066: a coherent-but-different authoritative fact after the release invalidates the old intent -------------------------
        ["authority-agent-outcome-source-changed-after-release"] = new(Step.Implementation, false, "attempts",
            ids => ("UPDATE attempts SET \"AgentOutcome\" = 'SourceChanged' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["authority-agent-outcome-source-changed-on-a-queued-review"] = new(Step.CodeReview, true, "attempts",
            ids => ("UPDATE attempts SET \"AgentOutcome\" = 'CheckpointEvidenceUnavailable' WHERE \"Id\" = {0}", [ids.OperationId])),
        ["authority-sequence-state-after-release"] = new(Step.CodeReview, false, "local_execution_sequences",
            ids => ("UPDATE local_execution_sequences SET \"State\" = 'Stopped' WHERE \"Id\" = {0}", [ids.SequenceId])),
        ["authority-observation-physical-exit-code"] = new(Step.CodeReview, false, "local_execution_termination_observations",
            ids => ("UPDATE local_execution_termination_observations SET \"PhysicalExitCode\" = \"PhysicalExitCode\" + 1 WHERE \"Id\" = {0}", [ids.ObservationId])),
        ["authority-observation-elapsed"] = new(Step.Verification, false, "local_execution_termination_observations",
            ids => ("UPDATE local_execution_termination_observations SET \"ObservedElapsed\" = \"ObservedElapsed\" - 1 WHERE \"Id\" = {0}", [ids.ObservationId])),
        ["authority-process-duration-of-the-operation-and-the-observation"] = Custom(Step.CodeReview, false, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "attempts", "UPDATE attempts SET \"AgentProcessDuration\" = \"AgentProcessDuration\" + 1 WHERE \"Id\" = {0}", ids.OperationId);
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "local_execution_termination_observations",
                "UPDATE local_execution_termination_observations SET \"ProcessDuration\" = \"ProcessDuration\" + 1 WHERE \"Id\" = {0}", ids.ObservationId);
        }),
        ["authority-dispatch-time-of-the-witness-and-the-operation"] = Custom(Step.CodeReview, false, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "attempts", "UPDATE attempts SET \"AgentDispatchedAtUtc\" = {0} WHERE \"Id\" = {1}", EarlierTime, ids.OperationId);
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "local_execution_dispatch_witnesses", "UPDATE local_execution_dispatch_witnesses SET \"DispatchedAtUtc\" = {0} WHERE \"Id\" = {1}", EarlierTime,
                ids.WitnessId);
        }),
        ["authority-source-fingerprint-everywhere-it-is-recorded"] = Custom(Step.CodeReview, false, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "git_checkpoints", "UPDATE git_checkpoints SET \"FingerprintSha256\" = {0} WHERE \"Id\" = {1}", OtherFingerprint, ids.SourceCheckpointId);
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "local_execution_sequence_steps", "UPDATE local_execution_sequence_steps SET \"SourceFingerprintSha256\" = {0} WHERE \"Id\" = {1}",
                OtherFingerprint, ids.StepId);
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "local_execution_dispatch_witnesses", "UPDATE local_execution_dispatch_witnesses SET \"SourceFingerprintSha256\" = {0} WHERE \"Id\" = {1}",
                OtherFingerprint, ids.WitnessId);
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "attempts", "UPDATE attempts SET \"AgentCheckpointFingerprintSha256\" = {0} WHERE \"Id\" = {1}", OtherFingerprint, ids.OperationId);
        }),
        ["authority-completion-fingerprint-of-a-source-changed-verification"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [new string('8', 64), ids.OperationId]),
            Prepare: PrepareSourceChangedVerificationAsync),
        ["authority-completion-fingerprint-malformed"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"CompletionFingerprintSha256\" = 'not-a-sha256' WHERE \"Id\" = {0}", [ids.OperationId]),
            Prepare: PrepareSourceChangedVerificationAsync),
        ["authority-completion-fingerprint-oversized"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [new string('9', 65), ids.OperationId]),
            Prepare: PrepareSourceChangedVerificationAsync),
        ["authority-completion-fingerprint-uppercase"] = new(Step.Verification, false, "verification_executions",
            ids => ("UPDATE verification_executions SET \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [new string('F', 64), ids.OperationId]),
            Prepare: PrepareSourceChangedVerificationAsync),
        ["authority-agent-outcome-on-a-queued-implementation"] = new(Step.Implementation, true, "attempts",
            ids => ("UPDATE attempts SET \"AgentOutcome\" = 'VerificationEvidenceChanged' WHERE \"Id\" = {0}", [ids.OperationId])),
        // ---- each independent boundary on its own: a snapshot that AGREES with the changed fact must still not release -------------------
        // The CHECK constraints of the release row are suspended for the snapshot write, so these rows are ones the typed snapshot and its
        // storage vocabulary would otherwise keep unrepresentable; the ownership expression and the SQL fence decide them by their own terms.
        ["agreeing-snapshot-clean-exit-agent-outcome-on-an-implementation"] = Custom(Step.Implementation, false, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "attempts", "UPDATE attempts SET \"AgentOutcome\" = 'Implemented' WHERE \"Id\" = {0}", ids.OperationId);
            await LocalExecutionOwnershipReleaseTamper.ApplyWithoutCheckConstraintsAsync(
                database, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"IntentAgentOutcome\" = 'Implemented' WHERE \"Id\" = {0}", ids.ReleaseId);
        }),
        ["agreeing-snapshot-approved-review-outcome-on-a-queued-review"] = Custom(Step.CodeReview, true, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "attempts", "UPDATE attempts SET \"AgentOutcome\" = 'ReviewApproved' WHERE \"Id\" = {0}", ids.OperationId);
            await LocalExecutionOwnershipReleaseTamper.ApplyWithoutCheckConstraintsAsync(
                database, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"IntentAgentOutcome\" = 'ReviewApproved' WHERE \"Id\" = {0}", ids.ReleaseId);
        }),
        ["agreeing-snapshot-failed-verification-status"] = Custom(Step.Verification, false, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "verification_executions", "UPDATE verification_executions SET \"Status\" = 'Failed' WHERE \"Id\" = {0}", ids.OperationId);
            await LocalExecutionOwnershipReleaseTamper.ApplyWithoutCheckConstraintsAsync(
                database, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"IntentOperationStatus\" = 'Failed' WHERE \"Id\" = {0}", ids.ReleaseId);
        }),
        ["agreeing-snapshot-source-changed-verification-with-a-matching-completion-fingerprint"] = Custom(Step.Verification, false, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "verification_executions", "UPDATE verification_executions SET \"Status\" = 'SourceChanged' WHERE \"Id\" = {0}", ids.OperationId);
            await LocalExecutionOwnershipReleaseTamper.ApplyWithoutCheckConstraintsAsync(
                database, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"IntentOperationStatus\" = 'SourceChanged' WHERE \"Id\" = {0}", ids.ReleaseId);
        }),
        ["agreeing-snapshot-timed-out-verification-with-a-differing-completion-fingerprint"] = Custom(Step.Verification, false, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(
                database, "verification_executions", "UPDATE verification_executions SET \"CompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}",
                OtherFingerprint, ids.OperationId);
            await LocalExecutionOwnershipReleaseTamper.ApplyWithoutCheckConstraintsAsync(
                database, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"IntentCompletionFingerprintSha256\" = {0} WHERE \"Id\" = {1}", OtherFingerprint, ids.ReleaseId);
        }),
        ["agreeing-expiry-columns-before-the-release-on-an-agent-attempt"] = Custom(Step.CodeReview, false, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyWithoutCheckConstraintsAsync(
                database, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"ConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"ReleasedAtUtc\", '-1 seconds') || '+00:00', "
                + "\"IntentConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"ReleasedAtUtc\", '-1 seconds') || '+00:00' WHERE \"Id\" = {0}",
                ids.ReleaseId);
        }),
        ["agreeing-expiry-columns-before-the-release-on-a-verification"] = Custom(Step.Verification, false, async (database, ids) =>
        {
            await LocalExecutionOwnershipReleaseTamper.ApplyWithoutCheckConstraintsAsync(
                database, "local_execution_ownership_releases",
                "UPDATE local_execution_ownership_releases SET \"ConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"ReleasedAtUtc\", '-1 seconds') || '+00:00', "
                + "\"IntentConfirmationExpiresAtUtc\" = strftime('%Y-%m-%d %H:%M:%S', \"ReleasedAtUtc\", '-1 seconds') || '+00:00' WHERE \"Id\" = {0}",
                ids.ReleaseId);
        }),
        ["authority-sequence-state-on-a-queued-verification"] = new(Step.Verification, true, "local_execution_sequences",
            ids => ("UPDATE local_execution_sequences SET \"State\" = 'Interrupted' WHERE \"Id\" = {0}", [ids.SequenceId])),
        ["pinned-recipe"] = new(Step.Verification, false, "local_execution_sequence_recipes",
            ids => ("UPDATE local_execution_sequence_recipes SET \"VerificationCommandId\" = {0} WHERE \"SequenceId\" = {1} "
                + "AND \"Ordinal\" = (SELECT \"RecipeOrdinal\" FROM local_execution_sequence_steps WHERE \"Id\" = {2})", [Guid.NewGuid(), ids.SequenceId, ids.StepId])),

        // ---- the step, the sequence and the queued admission --------------------------------------------------------------------------
        ["step-fingerprint"] = new(Step.CodeReview, false, "local_execution_sequence_steps",
            ids => ("UPDATE local_execution_sequence_steps SET \"SourceFingerprintSha256\" = {0} WHERE \"Id\" = {1}", [OtherFingerprint, ids.StepId])),
        ["sequence-live"] = new(Step.CodeReview, false, "local_execution_sequences",
            ids => ("UPDATE local_execution_sequences SET \"State\" = 'Stopping' WHERE \"Id\" = {0}", [ids.SequenceId])),
        ["admission-not-consumed"] = new(Step.Implementation, true, "local_execution_admissions",
            ids => ("UPDATE local_execution_admissions SET \"State\" = 'Reserved' WHERE \"Id\" = {0}", [ids.AdmissionId!.Value])),
        ["admission-step"] = new(Step.Implementation, true, "local_execution_admissions",
            ids => ("UPDATE local_execution_admissions SET \"StepId\" = {0} WHERE \"Id\" = {1}", [Guid.NewGuid(), ids.AdmissionId!.Value])),
        ["admission-source-checkpoint"] = new(Step.Implementation, true, "local_execution_admissions",
            ids => ("UPDATE local_execution_admissions SET \"SourceCheckpointId\" = {0} WHERE \"Id\" = {1}", [ids.OtherCheckpointId, ids.AdmissionId!.Value])),
    };

    private readonly SqliteDatabaseFixture fixture = new();

    /// <summary>The identity of the first stored row of the table that is not the release target: the donor of an identity collision.</summary>
    private static async Task<Guid> ReadFirstAsync(SqliteDatabaseFixture database, string table)
    {
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // The table name is a constant of this test and the database is disposable.
        command.CommandText = "SELECT \"Id\" FROM \"" + table + "\" ORDER BY rowid LIMIT 1;";
#pragma warning restore CA2100
        return Guid.Parse((string)(await command.ExecuteScalarAsync())!);
    }

    public static TheoryData<string> Cases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Catalog.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [InlineData(Step.CodeReview, false)]
    [InlineData(Step.Verification, false)]
    [InlineData(Step.Implementation, true)]
    public async Task A_coherent_release_frees_the_operation_in_the_read_the_ownership_readings_and_the_database_fence(Step kind, bool queued)
    {
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, kind, queued);
        var scene = ended.Scene;

        // While the quarantine holds, the write-seam probe of the live-fence guards is refused by the database itself.
        Assert.True(await LocalExecutionOwnershipReleaseTamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));

        await scene.ReleaseStepAsync(ended.Target.Id);

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Released, read.Status);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await LocalExecutionOwnershipReleaseRig.ReadOwnershipAsync(fixture, scene)).Workspace);
        Assert.False(await LocalExecutionOwnershipReleaseTamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));
        Assert.False(await LocalExecutionOwnershipReleaseTamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task One_changed_stored_fact_of_the_chain_is_never_a_release_anywhere(string name)
    {
        var tamper = Catalog[name];
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, tamper.Kind, tamper.Queued);
        var scene = ended.Scene;
        if (tamper.Prepare is { } prepare)
        {
            await prepare(fixture, ended.Target);
        }

        await scene.ReleaseStepAsync(ended.Target.Id);
        var ids = await LocalExecutionOwnershipReleaseTamper.ReadIdsAsync(fixture, scene, ended.Target);
        if (tamper.Custom is { } custom)
        {
            await custom(fixture, ids);
        }
        else
        {
            var (sql, parameters) = tamper.Build!(ids);
            await LocalExecutionOwnershipReleaseTamper.ApplyAsync(fixture, tamper.Table, sql, parameters);
        }

        var read = await scene.ReadReleaseAsync(ended.Target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, read.Status);
        Assert.Null(read.Release);
        Assert.Null(read.Candidate);
        var ownership = await LocalExecutionOwnershipReleaseRig.ReadOwnershipAsync(fixture, scene);
        Assert.NotEqual(WorkspaceExecutionStatus.Idle, ownership.Workspace);
        Assert.True(ownership.MutatingOwners is null or >= 1 || !LocalExecutionOwnershipReleaseRig.IsMutating(tamper.Kind), $"owners={ownership.MutatingOwners}");
        // Abandonment, in the actual scope of what is held: a run moved to another project is asked in that project, an operation that
        // belongs to its original project in that one, and the database write seam decides by the run itself, wherever its row points.
        var blockedByPolicy = ownership.AbandonmentBlocked;
        if (name.StartsWith("run-project", StringComparison.Ordinal))
        {
            blockedByPolicy |= (await LocalExecutionOwnershipReleaseRig.ReadAbandonmentBlockedAsync(fixture, MovedProjectId));
        }

        Assert.True(blockedByPolicy, "abandonment must stay blocked, in the actual scope of what is held, while the chain is not coherent");
        Assert.True(
            await LocalExecutionOwnershipReleaseTamper.ProbeRunAbandonmentIsBlockedAsync(fixture, scene.RunId),
            "the database refuses the abandonment of the run");

        Assert.True(await LocalExecutionOwnershipReleaseTamper.ProbeIsBlockedAsync(fixture, scene.ProjectId));
    }
}
