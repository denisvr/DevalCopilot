using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The whole historical proof of one admission-history snapshot (ADR-0063), over the rows of one statement and nothing else. The root is
/// the ManualAgent run's one sequence with its pinned project, workspace and initial checkpoint, a defined admission mode and valid
/// recipe and step counts. An Immediate sequence has no ledger. A queued ledger is nonempty and bounded by Domain's recipe-dependent step
/// maximum, with strictly increasing positive order numbers, unique gapless ordinals, the closed kind, pass, recipe, predecessor, report
/// and correction-subject shapes of Domain's mapping, and each source checkpoint and fingerprint in the pinned workspace. Every linked step
/// must agree with its admission on the whole tuple (sequence, ordinal, kind, pass, recipe, predecessor, source, report and subject),
/// and every recorded step of the sequence must have been linked by exactly one admission. Only Consumed admissions with a Concluded step
/// may precede the last; the last is one of Waiting, Reserved, Consumed, Retired or Unresolved and must fit the sequence's recorded state.
/// Any contradiction makes the whole answer Unavailable, never a partial list. Historical shapes only: nothing here asks whether a step
/// could execute now or whether any process lives.
/// </summary>
internal static class LocalExecutionAdmissionHistoryProof
{
    private const int MaximumRecipeNameLength = 200;

    public static LocalExecutionAdmissionHistoryReading Prove(LocalExecutionAdmissionHistoryRow[] rows)
    {
        var root = rows[0].Root;
        if (root.SequenceId is null)
        {
            return rows.Length == 1 && rows[0].Admission.Id is null
                ? LocalExecutionAdmissionHistoryReading.NotRecorded
                : LocalExecutionAdmissionHistoryReading.Unavailable;
        }

        if (rows.Any(row => row.Root != root) || !RootHolds(root))
        {
            return LocalExecutionAdmissionHistoryReading.Unavailable;
        }

        if (root.AdmissionModeCode == 1)
        {
            return rows.Length == 1 && rows[0].Admission.Id is null
                ? LocalExecutionAdmissionHistoryReading.NotQueued
                : LocalExecutionAdmissionHistoryReading.Unavailable;
        }

        var admissions = ProveLedger(rows, root);
        return admissions is null
            ? LocalExecutionAdmissionHistoryReading.Unavailable
            : LocalExecutionAdmissionHistoryReading.Recorded(Header(root), admissions);
    }

    private static LocalExecutionAdmissionHistoryHeader Header(LocalExecutionAdmissionHistoryRoot root) => new(
        root.SequenceId!.Value,
        (LocalExecutionAdmissionMode)(root.AdmissionModeCode - 1),
        (LocalExecutionSequenceState)(root.StateCode - 1),
        root.ReasonCode,
        root.Revision!.Value,
        root.ArmedAtUtc!.Value,
        root.AdvancementDeadlineUtc!.Value,
        root.UpdatedAtUtc!.Value,
        root.EndedAtUtc);

    private static bool RootHolds(LocalExecutionAdmissionHistoryRoot root)
    {
        if (root.RunId == Guid.Empty
            || RunExecutionModeStorage.Read(root.StoredMode) != RunExecutionMode.ManualAgent
            || root.SequenceId is not { } sequenceId || sequenceId == Guid.Empty
            || root.SequenceRunId != root.RunId
            || root.SequenceProjectId != root.RunProjectId
            || root.WorkspaceId is not { } workspaceId || workspaceId == Guid.Empty
            || root.WorkspaceProjectId != root.RunProjectId
            || root.CheckpointId is not { } checkpointId || checkpointId == Guid.Empty
            || !LocalExecutionSequencePolicy.IsLowercaseSha256(root.Fingerprint)
            || root.InitialCheckpointWorkspaceId != workspaceId
            || !string.Equals(root.InitialCheckpointFingerprint, root.Fingerprint, StringComparison.Ordinal)
            || root.StateCode is < 1 or > 10
            || root.AdmissionModeCode is < 1 or > 2
            || (root.StateCode == 10 && root.AdmissionModeCode != 2)
            || root.RecipeCount is not { } recipeCount
            || root.Revision is null
            || root.ReasonOversized
            || root.ArmedAtUtc is not { } armedAtUtc || !IsUtc(armedAtUtc)
            || root.UpdatedAtUtc is not { } updatedAtUtc || !IsUtc(updatedAtUtc) || updatedAtUtc < armedAtUtc
            || root.AdvancementDeadlineUtc != LocalExecutionSequencePolicy.AdvancementDeadlineOf(armedAtUtc)
            || root.RecipeRows != recipeCount
            || root.StepRows > LocalExecutionSequencePolicy.MaximumSteps(recipeCount))
        {
            return false;
        }

        var ended = LocalExecutionSequencePolicy.IsEnded((LocalExecutionSequenceState)(root.StateCode - 1));
        return ended == (root.EndedAtUtc is not null)
            && (ended ? LocalExecutionSequenceReason.IsKnown(root.ReasonCode) : root.ReasonCode is null)
            && (root.EndedAtUtc is not { } endedAtUtc || (IsUtc(endedAtUtc) && endedAtUtc >= armedAtUtc));
    }

    private static LocalExecutionInspectionAdmission[]? ProveLedger(
        LocalExecutionAdmissionHistoryRow[] rows, LocalExecutionAdmissionHistoryRoot root)
    {
        var recipeCount = root.RecipeCount!.Value;
        var sequenceState = (LocalExecutionSequenceState)(root.StateCode - 1);
        if (rows.Length > LocalExecutionSequencePolicy.MaximumSteps(recipeCount)
            || rows[0].Admission.Id is null
            || sequenceState == LocalExecutionSequenceState.Armed)
        {
            return null;
        }

        var admissions = new LocalExecutionInspectionAdmission[rows.Length];
        var admissionIds = new HashSet<Guid>();
        var stepIds = new HashSet<Guid>();
        var reservedOperationIds = new HashSet<Guid>();
        var memberships = new HashSet<(int Pass, int Recipe)>();
        var linkedOperations = new List<(LocalExecutionStepKind Kind, Guid OperationId)>();
        long previousNumber = 0;
        var linkedSteps = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            var admission = rows[index].Admission;
            var step = rows[index].Step;
            var reference = rows[index].Reference;
            var isLast = index == rows.Length - 1;
            if (admission.Id is not { } admissionId || admissionId == Guid.Empty || !admissionIds.Add(admissionId)
                || admission.SequenceId != root.SequenceId
                || admission.Number is not { } number || number <= previousNumber
                || admission.Ordinal != index
                || admission.KindCode is < 1 or > 6
                || admission.StateCode is < 1 or > 5
                || admission.WaitingReasonCode is < 1 or > 6
                || admission.PassNumber is not { } pass
                || admission.Revision is not { } revision
                || admission.ReasonOversized
                || admission.SourceCheckpointId is not { } sourceCheckpointId || sourceCheckpointId == Guid.Empty
                || !LocalExecutionSequencePolicy.IsLowercaseSha256(admission.SourceFingerprint)
                || admission.EnqueuedAtUtc is not { } enqueuedAtUtc || !IsUtc(enqueuedAtUtc)
                || admission.WaitingReasonRecordedAtUtc is not { } waitingAtUtc || !IsUtc(waitingAtUtc) || waitingAtUtc < enqueuedAtUtc
                || admission.UpdatedAtUtc is not { } updatedAtUtc || !IsUtc(updatedAtUtc) || updatedAtUtc < waitingAtUtc)
            {
                return null;
            }

            previousNumber = number;
            var kind = (LocalExecutionStepKind)(admission.KindCode - 1);
            var state = (LocalExecutionAdmissionState)(admission.StateCode - 1);
            var isImplementation = kind == LocalExecutionStepKind.Implementation;
            var isVerification = kind == LocalExecutionStepKind.Verification;
            var isCorrection = LocalExecutionSequencePolicy.IsCorrectionKind(kind);
            if ((LocalExecutionSequencePolicy.ContractOf(kind) is null) != isVerification
                || isImplementation != (pass == 0)
                || isImplementation != (index == 0)
                || isVerification != admission.RecipePresent
                || (isVerification && (admission.RecipeOrdinal is not { } position || position >= recipeCount))
                || isImplementation != (admission.PredecessorStepId is null)
                || (!isImplementation && (admission.PredecessorStepId is null || admission.PredecessorStepId != rows[index - 1].Admission.StepId))
                || isImplementation != (admission.ReportMessageId is null)
                || admission.ReportMessageId == Guid.Empty
                || isCorrection != (admission.SubjectAttemptId is not null)
                || admission.SubjectAttemptId == Guid.Empty
                || (isVerification && !memberships.Add((pass, admission.RecipeOrdinal!.Value))))
            {
                return null;
            }

            // The source checkpoint is in the pinned workspace with its recorded fingerprint; the implementation operates on the starting one.
            if (reference.SourceCheckpointWorkspaceId != root.WorkspaceId
                || !string.Equals(reference.SourceCheckpointFingerprint, admission.SourceFingerprint, StringComparison.Ordinal)
                || (isImplementation
                    && (sourceCheckpointId != root.CheckpointId
                        || !string.Equals(admission.SourceFingerprint, root.Fingerprint, StringComparison.Ordinal)))
                || (!isImplementation && (reference.ReportRunId != root.RunId || !reference.ReportIsExecutionReport))
                || (isVerification
                    && (reference.RecipeCommandId is not { } recipeId || recipeId == Guid.Empty
                        || reference.RecipeNameOversized
                        || string.IsNullOrWhiteSpace(reference.RecipeName)
                        || reference.RecipeName.Length > MaximumRecipeNameLength)))
            {
                return null;
            }

            // A correction names an earlier linked review (or diagnosis) attempt of the matching origin, never an arbitrary attempt.
            if (isCorrection)
            {
                var originKind = kind == LocalExecutionStepKind.ReviewCorrection
                    ? LocalExecutionStepKind.CodeReview
                    : LocalExecutionStepKind.VerificationDiagnosis;
                if (!linkedOperations.Any(earlier => earlier.Kind == originKind && earlier.OperationId == admission.SubjectAttemptId))
                {
                    return null;
                }
            }

            var linked = ProveLink(admission, step, state, pass, root, stepIds, reservedOperationIds);
            if (linked is null
                || !AdmissionFields(admission, state, linked.Value)
                || !PositionHolds(isLast, sequenceState, state, step.StateCode))
            {
                return null;
            }

            if (linked.Value)
            {
                linkedSteps++;
                if (step.OperationId is { } operationId)
                {
                    linkedOperations.Add((kind, operationId));
                }
            }

            admissions[index] = new LocalExecutionInspectionAdmission(
                admissionId,
                number,
                index,
                kind,
                pass,
                admission.RecipeOrdinal,
                isVerification ? reference.RecipeName : null,
                state,
                WaitingReason(admission.WaitingReasonCode),
                waitingAtUtc,
                enqueuedAtUtc,
                updatedAtUtc,
                revision,
                admission.ReasonCode,
                admission.StepId);
        }

        // Every recorded step of a queued sequence has its exact admission: no step exists that no admission linked.
        return linkedSteps == root.StepRows ? admissions : null;
    }

    /// <summary>Whether the admission's link and its step agree, as a tri-state: null when contradictory, otherwise whether a step is linked.
    /// A Waiting admission, or a Retired one that closed while still waiting, has no step row at all.</summary>
    private static bool? ProveLink(
        LocalExecutionAdmissionHistoryAdmission admission,
        LocalExecutionAdmissionHistoryStep step,
        LocalExecutionAdmissionState state,
        int pass,
        LocalExecutionAdmissionHistoryRoot root,
        HashSet<Guid> stepIds,
        HashSet<Guid> reservedOperationIds)
    {
        if (admission.StepId is null)
        {
            var closedWithoutStep = state is LocalExecutionAdmissionState.Waiting or LocalExecutionAdmissionState.Retired;
            return closedWithoutStep && step.Id is null ? false : null;
        }

        if (state == LocalExecutionAdmissionState.Waiting
            || admission.StepId == Guid.Empty
            || step.Id != admission.StepId
            || !stepIds.Add(admission.StepId.Value)
            || step.SequenceId != root.SequenceId
            || step.Ordinal != admission.Ordinal
            || step.KindCode != admission.KindCode
            || step.PassNumber != pass
            || step.RecipePresent != admission.RecipePresent
            || step.RecipeOrdinal != admission.RecipeOrdinal
            || step.PredecessorStepId != admission.PredecessorStepId
            || step.SourceCheckpointId != admission.SourceCheckpointId
            || !string.Equals(step.SourceFingerprint, admission.SourceFingerprint, StringComparison.Ordinal)
            || step.ReportMessageId != admission.ReportMessageId
            || step.SubjectAttemptId != admission.SubjectAttemptId
            || step.ReasonOversized
            || step.ReservedOperationId is not { } reservedOperationId || reservedOperationId == Guid.Empty
            || !reservedOperationIds.Add(reservedOperationId))
        {
            return null;
        }

        // The operation link a state allows, with its claim bookkeeping: only Consumed is linked, exactly to the identity reserved with it.
        var shapeHolds = state switch
        {
            LocalExecutionAdmissionState.Reserved => step.StateCode == 1 && Unlinked(step) && step.ReasonCode is null,
            LocalExecutionAdmissionState.Consumed => step.StateCode is 2 or 3
                && step.OperationId == reservedOperationId
                && step.ClaimedPresent
                && step.ResultDeadlinePresent
                && step.ReasonCode is null,
            LocalExecutionAdmissionState.Unresolved => step.StateCode == 5 && Unlinked(step)
                && LocalExecutionSequenceReason.IsKnown(step.ReasonCode),
            LocalExecutionAdmissionState.Retired => step.StateCode == 4 && Unlinked(step)
                && LocalExecutionSequenceReason.IsKnown(step.ReasonCode),
            _ => false,
        };
        return shapeHolds ? true : null;
    }

    private static bool Unlinked(LocalExecutionAdmissionHistoryStep step) =>
        step.OperationId is null && !step.ClaimedPresent && !step.ResultDeadlinePresent;

    /// <summary>The fixed reason of a Retired or Unresolved admission and none for any other, and the link a state requires.</summary>
    private static bool AdmissionFields(
        LocalExecutionAdmissionHistoryAdmission admission, LocalExecutionAdmissionState state, bool linked) =>
        state switch
        {
            LocalExecutionAdmissionState.Waiting => !linked && admission.ReasonCode is null,
            LocalExecutionAdmissionState.Reserved or LocalExecutionAdmissionState.Consumed =>
                linked && admission.ReasonCode is null,
            LocalExecutionAdmissionState.Unresolved => linked && LocalExecutionSequenceReason.IsKnown(admission.ReasonCode),
            LocalExecutionAdmissionState.Retired => LocalExecutionSequenceReason.IsKnown(admission.ReasonCode),
            _ => false,
        };

    /// <summary>Only a Consumed admission with a Concluded step may precede the last; the last fits the sequence's recorded state.</summary>
    private static bool PositionHolds(
        bool isLast, LocalExecutionSequenceState sequence, LocalExecutionAdmissionState state, int stepStateCode) =>
        isLast
            ? LifecycleHolds(sequence, state, stepStateCode)
            : state == LocalExecutionAdmissionState.Consumed && stepStateCode == 3;

    /// <summary>The last admission fits the sequence's recorded state: waiting while waiting, reserved while preparing, consumed while a
    /// claimed step is outstanding or draining, a concluded consumed step at an approval or escalation, and any consumed, retired or
    /// unresolved tail once the cycle stopped, needed attention or was interrupted.</summary>
    private static bool LifecycleHolds(LocalExecutionSequenceState sequence, LocalExecutionAdmissionState tail, int stepStateCode) =>
        sequence switch
        {
            LocalExecutionSequenceState.WaitingForAdmission => tail == LocalExecutionAdmissionState.Waiting,
            LocalExecutionSequenceState.PreparingStep => tail == LocalExecutionAdmissionState.Reserved,
            LocalExecutionSequenceState.AwaitingStepResult or LocalExecutionSequenceState.Stopping =>
                tail == LocalExecutionAdmissionState.Consumed,
            LocalExecutionSequenceState.ReadyForHumanApproval or LocalExecutionSequenceState.HumanEscalation =>
                tail == LocalExecutionAdmissionState.Consumed && stepStateCode == 3,
            LocalExecutionSequenceState.Stopped or LocalExecutionSequenceState.NeedsAttention
                or LocalExecutionSequenceState.Interrupted => tail is LocalExecutionAdmissionState.Consumed
                    or LocalExecutionAdmissionState.Retired or LocalExecutionAdmissionState.Unresolved,
            _ => false,
        };

    private static string WaitingReason(int code) => code switch
    {
        1 => LocalExecutionWaitingReason.AwaitingAdmissionCheck,
        2 => LocalExecutionWaitingReason.WaitingForWorkspace,
        3 => LocalExecutionWaitingReason.WaitingForCapacity,
        4 => LocalExecutionWaitingReason.WaitingForEarlierEligibleWork,
        5 => LocalExecutionWaitingReason.WaitingForPreparationCapacity,
        _ => LocalExecutionWaitingReason.Unavailable,
    };

    private static bool IsUtc(DateTimeOffset value) => value != default && value.Offset == TimeSpan.Zero;
}
