namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one durable authorization of an explicitly coordinated local execution sequence for a run (ADR-0060). It binds one exact plan,
/// project, workspace, starting checkpoint and fingerprint, the complete verification set, the run's requested settings and the
/// correction allowance, owns the closed <see cref="State"/> vocabulary and is never rearmed, retargeted, resumed or deleted by this
/// model. A unique RunId makes it a once-per-run-lifetime fact. <see cref="Revision"/> is the concurrency token every transition
/// advances, so two writers can never both apply a decision made from the same read.
/// </summary>
public sealed class LocalExecutionSequence
{
    public const int MaximumSettingsSnapshotLength = 2048;

    private LocalExecutionSequence()
    {
        FingerprintSha256 = string.Empty;
        ConfirmationSha256 = string.Empty;
        PlanInputsSha256 = string.Empty;
        SettingsSnapshot = string.Empty;
    }

    public static LocalExecutionSequence Arm(
        Guid id,
        Guid runId,
        Guid projectId,
        Guid workspaceId,
        Guid checkpointId,
        string fingerprintSha256,
        Guid planProposalMessageId,
        LocalExecutionPlanAuthority planAuthority,
        string planInputsSha256,
        string confirmationSha256,
        string settingsSnapshot,
        int recipeCount,
        int correctionAllowance,
        DateTimeOffset armedAtUtc)
    {
        if (id == Guid.Empty || runId == Guid.Empty || projectId == Guid.Empty || workspaceId == Guid.Empty
            || checkpointId == Guid.Empty || planProposalMessageId == Guid.Empty)
        {
            throw new ArgumentException("A local execution sequence requires durable identifiers.");
        }

        if (!LocalExecutionSequencePolicy.IsLowercaseSha256(fingerprintSha256)
            || !LocalExecutionSequencePolicy.IsLowercaseSha256(confirmationSha256)
            || !LocalExecutionSequencePolicy.IsLowercaseSha256(planInputsSha256))
        {
            throw new ArgumentException("The fingerprint and confirmation bindings must be lowercase hexadecimal SHA-256 values.");
        }

        if (!Enum.IsDefined(planAuthority))
        {
            throw new ArgumentOutOfRangeException(nameof(planAuthority));
        }

        if (string.IsNullOrEmpty(settingsSnapshot) || settingsSnapshot.Length > MaximumSettingsSnapshotLength)
        {
            throw new ArgumentException("The pinned settings snapshot is required and bounded.", nameof(settingsSnapshot));
        }

        if (!LocalExecutionSequencePolicy.IsRecipeCountValid(recipeCount))
        {
            throw new ArgumentOutOfRangeException(nameof(recipeCount));
        }

        if (correctionAllowance is < 0 or > LocalExecutionSequencePolicy.MaximumCorrections)
        {
            throw new ArgumentOutOfRangeException(nameof(correctionAllowance));
        }

        if (armedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(armedAtUtc));
        }

        return new LocalExecutionSequence
        {
            Id = id,
            RunId = runId,
            ProjectId = projectId,
            WorkspaceId = workspaceId,
            CheckpointId = checkpointId,
            FingerprintSha256 = fingerprintSha256,
            PlanProposalMessageId = planProposalMessageId,
            PlanAuthority = planAuthority,
            PlanInputsSha256 = planInputsSha256,
            ConfirmationSha256 = confirmationSha256,
            SettingsSnapshot = settingsSnapshot,
            RecipeCount = recipeCount,
            CorrectionAllowance = correctionAllowance,
            State = LocalExecutionSequenceState.Armed,
            Revision = 1,
            ArmedAtUtc = armedAtUtc,
            AdvancementDeadlineUtc = LocalExecutionSequencePolicy.AdvancementDeadlineOf(armedAtUtc),
            UpdatedAtUtc = armedAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    /// <summary>The starting checkpoint. Later steps evolve their own source from exact linked results, never from this row.</summary>
    public Guid CheckpointId { get; private set; }

    public string FingerprintSha256 { get; private set; }

    public Guid PlanProposalMessageId { get; private set; }

    public LocalExecutionPlanAuthority PlanAuthority { get; private set; }

    /// <summary>The lowercase SHA-256 of the plan's ordered input message identities (and Human grant, when one authorized it), pinned at arming.</summary>
    public string PlanInputsSha256 { get; private set; }

    /// <summary>The versioned SHA-256 confirmation value the Human submitted; a binding to the availability read, not a credential.</summary>
    public string ConfirmationSha256 { get; private set; }

    /// <summary>The canonical, versioned text of the run's pinned requested settings, stop policies and budget policies.</summary>
    public string SettingsSnapshot { get; private set; }

    public int RecipeCount { get; private set; }

    /// <summary>The smaller of two and the run's remaining shared correction allowance, pinned at arming.</summary>
    public int CorrectionAllowance { get; private set; }

    public LocalExecutionSequenceState State { get; private set; }

    public string? ReasonCode { get; private set; }

    public string? DetailCode { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset ArmedAtUtc { get; private set; }

    public DateTimeOffset AdvancementDeadlineUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? StopRequestedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    /// <summary>The exact linked report an approved review or an escalation answered; set only when the sequence ends so.</summary>
    public Guid? FinalReportMessageId { get; private set; }

    /// <summary>The exact linked review attempt that approved; set only at ReadyForHumanApproval.</summary>
    public Guid? FinalReviewAttemptId { get; private set; }

    /// <summary>The Escalation message (host exhaustion message or the preserved provider diagnosis escalation) that ended the cycle.</summary>
    public Guid? FinalEscalationMessageId { get; private set; }

    public bool IsAuthorityLive => LocalExecutionSequencePolicy.IsAuthorityLive(State);

    public void BeginPreparing(DateTimeOffset nowUtc)
    {
        if (State is not (LocalExecutionSequenceState.Armed or LocalExecutionSequenceState.AwaitingStepResult))
        {
            throw new InvalidOperationException($"A step cannot be reserved while the sequence is {State}.");
        }

        Advance(LocalExecutionSequenceState.PreparingStep, nowUtc);
    }

    public void MarkAwaitingResult(DateTimeOffset nowUtc)
    {
        if (State != LocalExecutionSequenceState.PreparingStep)
        {
            throw new InvalidOperationException($"A claim cannot be linked while the sequence is {State}.");
        }

        Advance(LocalExecutionSequenceState.AwaitingStepResult, nowUtc);
    }

    public void BeginStopping(DateTimeOffset nowUtc)
    {
        if (State != LocalExecutionSequenceState.AwaitingStepResult)
        {
            throw new InvalidOperationException($"A claimed step is not outstanding while the sequence is {State}.");
        }

        StopRequestedAtUtc = nowUtc;
        Advance(LocalExecutionSequenceState.Stopping, nowUtc);
    }

    /// <summary>Ends the sequence in a terminal or attention state with a fixed reason. Never leaves an ended state.</summary>
    public void End(
        LocalExecutionSequenceState endState,
        string reasonCode,
        string? detailCode,
        DateTimeOffset nowUtc,
        Guid? finalReportMessageId = null,
        Guid? finalReviewAttemptId = null,
        Guid? finalEscalationMessageId = null)
    {
        if (!LocalExecutionSequencePolicy.IsEnded(endState))
        {
            throw new ArgumentOutOfRangeException(nameof(endState), endState, "Not an ended state.");
        }

        if (!LocalExecutionSequenceReason.IsKnown(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode), "Not a fixed local execution sequence reason.");
        }

        if (!IsAuthorityLive)
        {
            throw new InvalidOperationException($"The sequence already ended as {State}.");
        }

        if (endState == LocalExecutionSequenceState.ReadyForHumanApproval
            && (finalReportMessageId is null || finalReviewAttemptId is null || finalEscalationMessageId is not null))
        {
            throw new ArgumentException("An approved review requires its exact report and review attempt.");
        }

        if (endState == LocalExecutionSequenceState.HumanEscalation
            && (finalReportMessageId is null || finalEscalationMessageId is null || finalReviewAttemptId is not null))
        {
            throw new ArgumentException("An escalation requires its exact report and escalation message.");
        }

        if (endState is not (LocalExecutionSequenceState.ReadyForHumanApproval or LocalExecutionSequenceState.HumanEscalation)
            && (finalReportMessageId is not null || finalReviewAttemptId is not null || finalEscalationMessageId is not null))
        {
            throw new ArgumentException("Only an approved or escalated end carries final references.");
        }

        if (endState == LocalExecutionSequenceState.Stopped && StopRequestedAtUtc is null)
        {
            StopRequestedAtUtc = nowUtc;
        }

        ReasonCode = reasonCode;
        DetailCode = LocalExecutionSequenceReason.NormalizeDetail(detailCode);
        EndedAtUtc = nowUtc;
        FinalReportMessageId = finalReportMessageId;
        FinalReviewAttemptId = finalReviewAttemptId;
        FinalEscalationMessageId = finalEscalationMessageId;
        Advance(endState, nowUtc);
    }

    private void Advance(LocalExecutionSequenceState next, DateTimeOffset nowUtc)
    {
        if (nowUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc));
        }

        State = next;
        Revision++;
        UpdatedAtUtc = nowUtc;
    }
}
