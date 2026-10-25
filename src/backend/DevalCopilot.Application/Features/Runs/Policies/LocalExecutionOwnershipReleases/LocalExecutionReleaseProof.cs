using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// Proves the one release statement from its single database snapshot (ADR-0065), entirely in memory and without any other read. It is
/// the reading twin of <see cref="LocalExecutionReleaseChain"/> (the ownership predicate and the SQL fence predicate): a release is
/// Released only when the same complete chain holds, and a candidate is Available only when the same chain would hold once the Human
/// confirmed it. Order is deliberate: an absent or foreign run, sequence or step is NotFound; an existing receipt is validated for replay
/// BEFORE the first-release eligibility check (an invalid stored release is Unavailable, never a replay or a silent new release);
/// coherent histories that retain no releasable quarantine are NotEligible; missing, incomplete, foreign or ambiguous evidence is
/// Unavailable and the hold stays. Nothing here reads Git, a process, a provider, a lease, the current workspace readiness or the latest
/// checkpoint: exact historical identity only.
/// </summary>
internal static class LocalExecutionReleaseProof
{
    private const int StoppedCode = 7;
    private const int NeedsAttentionCode = 8;
    private const int InterruptedCode = 9;

    public static LocalExecutionReleaseReading Prove(IReadOnlyList<LocalExecutionReleaseRow> rows)
    {
        if (rows.Count == 0 || rows.Any(candidate => candidate.Root.SequenceId is null) || rows.All(candidate => candidate.Step.Id is null))
        {
            return LocalExecutionReleaseReading.NotFound;
        }

        if (rows.Count != 1)
        {
            return LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
        }

        var row = rows[0];

        // A nullable numeric fact whose raw storage is neither NULL nor a supported integer was projected as the legal null: before any
        // eligibility, replay or chain comparison could count that null as supported history, the whole reading is Unavailable.
        if (!row.StorageValid)
        {
            return LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
        }

        if (!RootHolds(row.Root) || KindOf(row.Step.KindCode) is not { } kind || !StepHolds(row.Root, row.Step, kind))
        {
            return LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
        }

        // A reservation, an unresolved preparation or a revoked step owns no operation and is never released here.
        if (row.Step.OperationId is not { } operationId
            || row.Step.StateCode is not (2 or 3))
        {
            return LocalExecutionReleaseReading.NotEligible(LocalExecutionReleaseReason.StepNotLinked);
        }

        var operation = OperationOf(row, kind, operationId);
        if (operation is null)
        {
            return LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
        }

        // Replay is validated before the first-release eligibility: a stored receipt is either a coherent one or Unavailable.
        if (row.Receipt.Id is not null || row.Receipt.WitnessKeyedId is not null)
        {
            return ProveReleased(row, kind, operationId, operation);
        }

        if (operation.IsRunning)
        {
            return LocalExecutionReleaseReading.NotEligible(LocalExecutionReleaseReason.OperationNotTerminal);
        }

        if (operation.DispatchedAtUtc is null)
        {
            return LocalExecutionReleaseReading.NotEligible(LocalExecutionReleaseReason.NotDispatched);
        }

        if (operation.ExitProofHolds)
        {
            return LocalExecutionReleaseReading.NotEligible(LocalExecutionReleaseReason.AlreadyFree);
        }

        if (!operation.IsRecognizedTerminal)
        {
            return LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
        }

        if (row.Root.StateCode is not (StoppedCode or NeedsAttentionCode or InterruptedCode))
        {
            return LocalExecutionReleaseReading.NotEligible(LocalExecutionReleaseReason.SequenceNotEnded);
        }

        if (row.Witness.Id is null || row.Observation.Id is null)
        {
            return LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceMissing);
        }

        return ChainFacts(row, kind, operationId, operation) is { } facts
            ? LocalExecutionReleaseReading.Available(facts)
            : LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
    }

    private static LocalExecutionReleaseReading ProveReleased(
        LocalExecutionReleaseRow row, LocalExecutionStepKind kind, Guid operationId, OperationFacts operation)
    {
        var receipt = row.Receipt;
        if (ChainFacts(row, kind, operationId, operation) is not { } facts
            || !operation.IsRecognizedTerminal
            || row.Root.StateCode is not (StoppedCode or NeedsAttentionCode or InterruptedCode)
            || receipt.Id is not { } releaseId
            || receipt.WitnessKeyedId != releaseId
            || receipt.WitnessId != row.Witness.Id
            || receipt.ObservationId != row.Observation.Id
            || receipt.StepId != row.Step.Id
            || receipt.EventId is not { } eventId
            || receipt.EventHeaderId != eventId
            || receipt.EventRunId != row.Root.RunId
            || !receipt.EventIsRelease
            || !receipt.EventIsHuman
            || receipt.EventSequence is not { } eventSequence
            || receipt.ConfirmationVersion != LocalExecutionOwnershipReleaseConfirmation.CurrentVersion
            || !LocalExecutionSequencePolicy.IsLowercaseSha256(receipt.ConfirmationSha256)
            || receipt.ReleasedAtUtc is not { } releasedAtUtc
            || receipt.ConfirmationExpiresAtUtc is not { } expiresAtUtc
            || releasedAtUtc < facts.RecordedAtUtc
            || expiresAtUtc <= releasedAtUtc
            || expiresAtUtc - releasedAtUtc > LocalExecutionOwnershipReleaseConfirmation.MaximumLifetime
            || !IntentEquals(receipt.Intent, facts, receipt.ConfirmationVersion.Value, expiresAtUtc))
        {
            return LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
        }

        // ADR-0066: only now, with the whole typed snapshot, the exact expiry, the physical chain and the Human event intact, may a
        // well-shaped substituted checksum make the reading Unavailable without the ownership relational proof (which has no hashing
        // capability and does not depend on the checksum) reacquiring the owner. The initial confirmation and every coherent replay
        // still require the digest to verify.
        if (!string.Equals(
                receipt.ConfirmationSha256, LocalExecutionOwnershipReleaseConfirmation.Digest(facts, expiresAtUtc), StringComparison.Ordinal))
        {
            return LocalExecutionReleaseReading.Unavailable(LocalExecutionReleaseReason.EvidenceIncoherent);
        }

        return LocalExecutionReleaseReading.Released(
            facts,
            new LocalExecutionReleaseRecorded(
                releaseId, releasedAtUtc, eventSequence, receipt.ConfirmationVersion.Value, receipt.ConfirmationSha256!, expiresAtUtc));
    }

    /// <summary>The complete chain of witness, observation and operation (without a receipt): every identity, the whole step tuple, the
    /// dispatch time, the queued admission (exactly Consumed) or its absence and the matching terminal process tuple. Null when any part
    /// is missing, foreign or contradictory.</summary>
    private static LocalExecutionOwnershipReleaseFacts? ChainFacts(
        LocalExecutionReleaseRow row, LocalExecutionStepKind kind, Guid operationId, OperationFacts operation)
    {
        var root = row.Root;
        var step = row.Step;
        var witness = row.Witness;
        var observation = row.Observation;
        var isVerification = kind == LocalExecutionStepKind.Verification;
        var queued = root.AdmissionModeCode == 2;
        if (!operation.ResultConsistent
            || witness.Id is not { } witnessId
            || observation.Id is not { } observationId
            || witness.StepId != step.Id
            || witness.SequenceId != root.SequenceId
            || witness.RunId != root.RunId
            || witness.ProjectId != root.SequenceProjectId
            || witness.WorkspaceId != root.SequenceWorkspaceId
            || witness.FamilyCode != (isVerification ? 2 : 1)
            || (isVerification
                ? witness.VerificationExecutionId != operationId || witness.AgentAttemptId is not null
                    || witness.RecipeCommandId is null || witness.RecipeCommandId != step.RecipeCommandId || witness.ContractCode != 0
                : witness.AgentAttemptId != operationId || witness.VerificationExecutionId is not null
                    || witness.ContractCode != ContractCodeOf(kind) || witness.RecipeCommandId is not null)
            || KindOf(witness.KindCode) != kind
            || witness.Ordinal != step.Ordinal
            || witness.PassNumber != step.PassNumber
            || witness.RecipeOrdinal != step.RecipeOrdinal
            || witness.PredecessorStepId != step.PredecessorStepId
            || witness.SourceCheckpointId != step.SourceCheckpointId
            || !string.Equals(witness.SourceFingerprint, step.SourceFingerprint, StringComparison.Ordinal)
            || witness.ReportMessageId != step.ReportMessageId
            || witness.SubjectAttemptId != step.SubjectAttemptId
            || witness.DispatchedAtUtc is not { } dispatchedAtUtc
            || dispatchedAtUtc != operation.DispatchedAtUtc)
        {
            return null;
        }

        if (queued)
        {
            var admission = row.Operation;
            if (witness.AdmissionId is null
                || admission.AdmissionId != witness.AdmissionId
                || admission.AdmissionSequenceId != root.SequenceId
                || admission.AdmissionStepId != step.Id
                || admission.AdmissionStateCode != 3
                || admission.AdmissionOrdinal != step.Ordinal
                || KindOf(admission.AdmissionKindCode) != kind
                || admission.AdmissionPassNumber != step.PassNumber
                || admission.AdmissionRecipeOrdinal != step.RecipeOrdinal
                || admission.AdmissionSourceCheckpointId != step.SourceCheckpointId)
            {
                return null;
            }
        }
        else if (witness.AdmissionId is not null || row.Operation.AdmissionId is not null)
        {
            return null;
        }

        if (observation.WitnessId != witnessId
            || observation.OutcomeCode is not (1 or 2 or 3)
            || observation.ProcessDuration is not { } processDuration
            || observation.ObservedElapsed is not { } observedElapsed
            || observation.PhysicalExitCode is not { } physicalExitCode
            || observation.RecordedAtUtc is not { } recordedAtUtc
            || recordedAtUtc < dispatchedAtUtc
            || observation.OutcomeCode != operation.ProcessOutcomeCode
            || observation.SemanticExitCode != operation.ProcessExitCode
            || (!isVerification && operation.ProcessDuration != processDuration)
            || !LocalExecutionTerminationObservation.IsCoherent(
                (ProcessOutcome)(observation.OutcomeCode - 1), observation.SemanticExitCode, processDuration, physicalExitCode, observedElapsed))
        {
            return null;
        }

        return new LocalExecutionOwnershipReleaseFacts(
            witnessId,
            observationId,
            isVerification ? LocalExecutionOperationFamily.Verification : LocalExecutionOperationFamily.Agent,
            operationId,
            root.SequenceId!.Value,
            root.RunId,
            root.SequenceProjectId!.Value,
            root.SequenceWorkspaceId!.Value,
            (LocalExecutionSequenceState)(root.StateCode - 1),
            queued ? LocalExecutionAdmissionMode.WaitForAdmission : LocalExecutionAdmissionMode.Immediate,
            witness.AdmissionId,
            step.Id!.Value,
            kind,
            step.Ordinal!.Value,
            step.PassNumber!.Value,
            step.RecipeOrdinal,
            step.PredecessorStepId,
            step.SourceCheckpointId!.Value,
            step.SourceFingerprint!,
            step.ReportMessageId,
            step.SubjectAttemptId,
            isVerification ? null : ContractOf(kind),
            witness.RecipeCommandId,
            operation.StatusName,
            operation.AgentOutcome,
            operation.CompletionFingerprint,
            (ProcessOutcome)(observation.OutcomeCode - 1),
            observation.SemanticExitCode,
            processDuration.Ticks,
            physicalExitCode,
            observedElapsed.Ticks,
            dispatchedAtUtc,
            recordedAtUtc);
    }

    /// <summary>
    /// ADR-0066: whether the typed snapshot stored in the receipt is, field for field, the current complete proved tuple and the stored
    /// confirmation version and exact expiry: every identity, the sequence state and mode, the whole step and source tuple, the concrete
    /// terminal result (the Agent outcome or the verification completion fingerprint, never a coherence class), the process and physical
    /// facts and both recording times. A missing member, an unsupported version or any difference is a mismatch.
    /// </summary>
    private static bool IntentEquals(
        LocalExecutionReleaseIntent intent, LocalExecutionOwnershipReleaseFacts facts, int confirmationVersion, DateTimeOffset expiresAtUtc) =>
        intent.Version == LocalExecutionOwnershipRelease.CurrentIntentVersion
        && intent.Family == facts.Family
        && intent.OperationId == facts.OperationId
        && intent.SequenceId == facts.SequenceId
        && intent.RunId == facts.RunId
        && intent.ProjectId == facts.ProjectId
        && intent.WorkspaceId == facts.WorkspaceId
        && intent.SequenceState == facts.SequenceState
        && intent.AdmissionMode == facts.AdmissionMode
        && intent.AdmissionId == facts.AdmissionId
        && intent.Kind == facts.Kind
        && intent.Ordinal == facts.Ordinal
        && intent.PassNumber == facts.PassNumber
        && intent.RecipeOrdinal == facts.RecipeOrdinal
        && intent.PredecessorStepId == facts.PredecessorStepId
        && intent.SourceCheckpointId == facts.SourceCheckpointId
        && string.Equals(intent.SourceFingerprintSha256, facts.SourceFingerprintSha256, StringComparison.Ordinal)
        && intent.ReportMessageId == facts.ReportMessageId
        && intent.SubjectAttemptId == facts.SubjectAttemptId
        && intent.AgentResponseContract == facts.AgentResponseContract
        && intent.RecipeCommandId == facts.RecipeCommandId
        && string.Equals(intent.OperationStatus, facts.OperationStatus, StringComparison.Ordinal)
        && intent.AgentOutcome == facts.AgentOutcome
        && string.Equals(intent.CompletionFingerprintSha256, facts.CompletionFingerprintSha256, StringComparison.Ordinal)
        && intent.ProcessOutcome == facts.ProcessOutcome
        && intent.SemanticExitCode == facts.SemanticExitCode
        && intent.ProcessDuration?.Ticks == facts.ProcessDurationTicks
        && intent.PhysicalExitCode == facts.PhysicalExitCode
        && intent.ObservedElapsed?.Ticks == facts.ObservedElapsedTicks
        && intent.DispatchedAtUtc == facts.DispatchedAtUtc
        && intent.ObservationRecordedAtUtc == facts.RecordedAtUtc
        && intent.ConfirmationVersion == confirmationVersion
        && intent.ConfirmationExpiresAtUtc == expiresAtUtc;

    private static bool RootHolds(LocalExecutionReleaseRoot root) =>
        root.SequenceId is not null
        && root.RunIsManualAgent
        && root.SequenceRunId == root.RunId
        && root.SequenceProjectId == root.RunProjectId
        && root.SequenceWorkspaceId is not null
        && root.WorkspaceProjectId == root.SequenceProjectId
        && root.StateCode is >= 1 and <= 10
        && root.AdmissionModeCode is 1 or 2
        && root.RecipeCount is { } recipes
        && root.RecipeRows == recipes
        && root.StepRows <= LocalExecutionSequencePolicy.MaximumSteps(recipes);

    private static bool StepHolds(LocalExecutionReleaseRoot root, LocalExecutionReleaseStep step, LocalExecutionStepKind kind)
    {
        var isImplementation = kind == LocalExecutionStepKind.Implementation;
        var isVerification = kind == LocalExecutionStepKind.Verification;
        var isCorrection = LocalExecutionSequencePolicy.IsCorrectionKind(kind);
        return step.SequenceId == root.SequenceId
            && step.Ordinal is { } ordinal
            && step.PassNumber is { } pass
            && step.StateCode != 0
            && step.SourceCheckpointId is not null
            && LocalExecutionSequencePolicy.IsLowercaseSha256(step.SourceFingerprint)
            && step.SourceCheckpointWorkspaceId == root.SequenceWorkspaceId
            && string.Equals(step.SourceCheckpointFingerprint, step.SourceFingerprint, StringComparison.Ordinal)
            && isImplementation == (pass == 0)
            && isImplementation == (ordinal == 0)
            && isVerification == step.HasRecipe
            && (!isVerification || (step.RecipeOrdinal is not null && step.RecipeCommandId is not null))
            && isImplementation == (step.PredecessorStepId is null)
            && isImplementation == (step.ReportMessageId is null)
            && isCorrection == (step.SubjectAttemptId is not null);
    }

    private static OperationFacts? OperationOf(LocalExecutionReleaseRow row, LocalExecutionStepKind kind, Guid operationId)
    {
        var step = row.Step;
        var root = row.Root;
        var operation = row.Operation;
        if (kind == LocalExecutionStepKind.Verification)
        {
            // The execution of the step's own project, workspace, source and pinned recipe; no Agent row may share the identity.
            if (operation.ExecutionId != operationId || operation.AgentId is not null
                || operation.ExecutionProjectId != root.SequenceProjectId
                || operation.ExecutionWorkspaceId != root.SequenceWorkspaceId
                || operation.ExecutionCheckpointId != step.SourceCheckpointId
                || !string.Equals(operation.ExecutionFingerprint, step.SourceFingerprint, StringComparison.Ordinal)
                || operation.ExecutionCommandId != step.RecipeCommandId
                || operation.ExecutionStatusCode == 0
                || operation.ExecutionOutcomeCode == 99)
            {
                return null;
            }

            var status = operation.ExecutionStatusCode;
            var outcome = operation.ExecutionOutcomeCode;
            var completionMatches = string.Equals(operation.ExecutionCompletionFingerprint, operation.ExecutionFingerprint, StringComparison.Ordinal);
            var consistent = outcome is 2 or 3
                && operation.ExecutionExitCode is null
                && LocalExecutionSequencePolicy.IsLowercaseSha256(operation.ExecutionCompletionFingerprint)
                && ((status == 4 && outcome == 2 && completionMatches)
                    || (status == 5 && outcome == 3 && completionMatches)
                    || (status == 7 && !completionMatches));
            return new OperationFacts(
                status == 1,
                operation.ExecutionDispatchedAtUtc,
                outcome == 1 && operation.ExecutionExitCode is not null,
                status is 3 or 4 or 5 or 6 or 7,
                status switch
                {
                    2 => "Passed",
                    3 => "Failed",
                    4 => "TimedOut",
                    5 => "Cancelled",
                    6 => "Interrupted",
                    7 => "SourceChanged",
                    _ => "Running",
                },
                outcome is 1 or 2 or 3 ? outcome : 0,
                operation.ExecutionExitCode,
                null,
                consistent,
                null,
                operation.ExecutionCompletionFingerprint);
        }

        if (operation.AgentId != operationId || operation.ExecutionId is not null
            || operation.AgentRunId != root.RunId
            || operation.AgentWorkspaceId != root.SequenceWorkspaceId
            || operation.AgentCheckpointId != step.SourceCheckpointId
            || operation.AgentContractCode != ContractCodeOf(kind)
            || operation.AgentStatusCode == 0
            || operation.AgentProcessOutcomeCode == 99)
        {
            return null;
        }

        var agentExit = operation.AgentProcessOutcomeCode == 1
            && operation.AgentProcessExitCode is not null
            && operation.AgentProcessDuration is { } duration
            && duration >= TimeSpan.Zero;
        var agentConsistent = operation.AgentStatusCode == 3
            && operation.AgentOutcomeCode == 1
            && operation.AgentProcessOutcomeCode is 2 or 3
            && operation.AgentProcessExitCode is null
            && operation.AgentProcessDuration is { } processDuration
            && processDuration >= TimeSpan.Zero
            && string.Equals(operation.AgentCheckpointFingerprint, step.SourceFingerprint, StringComparison.Ordinal);
        return new OperationFacts(
            operation.AgentStatusCode == 1,
            operation.AgentDispatchedAtUtc,
            agentExit,
            operation.AgentStatusCode is 2 or 3 or 4,
            operation.AgentStatusCode switch
            {
                2 => "Completed",
                3 => "Failed",
                4 => "Interrupted",
                _ => "Running",
            },
            operation.AgentProcessOutcomeCode,
            operation.AgentProcessExitCode,
            operation.AgentProcessDuration,
            agentConsistent,
            operation.AgentOutcomeValue,
            null);
    }

    private static LocalExecutionStepKind? KindOf(int code) => code switch
    {
        1 => LocalExecutionStepKind.Implementation,
        2 => LocalExecutionStepKind.Verification,
        3 => LocalExecutionStepKind.CodeReview,
        4 => LocalExecutionStepKind.VerificationDiagnosis,
        5 => LocalExecutionStepKind.ReviewCorrection,
        6 => LocalExecutionStepKind.DiagnosisCorrection,
        _ => null,
    };

    private static int ContractCodeOf(LocalExecutionStepKind kind) => kind switch
    {
        LocalExecutionStepKind.Implementation => 1,
        LocalExecutionStepKind.CodeReview => 2,
        LocalExecutionStepKind.ReviewCorrection or LocalExecutionStepKind.DiagnosisCorrection => 3,
        LocalExecutionStepKind.VerificationDiagnosis => 4,
        _ => 0,
    };

    private static AgentResponseContract? ContractOf(LocalExecutionStepKind kind) => LocalExecutionSequencePolicy.ContractOf(kind);

    /// <summary>The proved facts of the linked operation row: its status class, dispatch time, ADR-0060's ordinary Exited proof, the
    /// stored terminal process tuple the observation must equal and whether its recorded result is consistent under the existing result
    /// policies (<see cref="LocalExecutionReleaseTerminal"/>), not merely a member of a terminal-status vocabulary.</summary>
    private sealed record OperationFacts(
        bool IsRunning,
        DateTimeOffset? DispatchedAtUtc,
        bool ExitProofHolds,
        bool IsRecognizedTerminal,
        string StatusName,
        int ProcessOutcomeCode,
        int? ProcessExitCode,
        TimeSpan? ProcessDuration,
        bool ResultConsistent,
        AgentOutcome? AgentOutcome,
        string? CompletionFingerprint);
}
