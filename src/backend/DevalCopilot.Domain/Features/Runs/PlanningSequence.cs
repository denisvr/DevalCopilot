namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one durable authorization of an explicitly coordinated read-only planning sequence for a run (ADR-0055). It binds the run's
/// recorded objective and one exact project, workspace, checkpoint and fingerprint, owns the closed <see cref="State"/> vocabulary and
/// is never rearmed, retargeted, resumed or deleted by this model. A unique RunId makes it a once-per-run-lifetime fact.
/// <see cref="Revision"/> is the concurrency token every transition advances, so two writers can never both apply a decision made
/// from the same read.
/// </summary>
public sealed class PlanningSequence
{
    private PlanningSequence()
    {
        FingerprintSha256 = string.Empty;
        ObjectiveSha256 = string.Empty;
    }

    public static PlanningSequence Arm(
        Guid id,
        Guid runId,
        Guid projectId,
        Guid workspaceId,
        Guid checkpointId,
        string fingerprintSha256,
        string objectiveSha256,
        DateTimeOffset armedAtUtc)
    {
        if (id == Guid.Empty || runId == Guid.Empty || projectId == Guid.Empty || workspaceId == Guid.Empty || checkpointId == Guid.Empty)
        {
            throw new ArgumentException("A planning sequence requires durable identifiers.");
        }

        if (!PlanningSequencePolicy.IsLowercaseSha256(fingerprintSha256) || !PlanningSequencePolicy.IsLowercaseSha256(objectiveSha256))
        {
            throw new ArgumentException("The fingerprint and objective bindings must be lowercase hexadecimal SHA-256 values.");
        }

        if (armedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(armedAtUtc));
        }

        return new PlanningSequence
        {
            Id = id,
            RunId = runId,
            ProjectId = projectId,
            WorkspaceId = workspaceId,
            CheckpointId = checkpointId,
            FingerprintSha256 = fingerprintSha256,
            ObjectiveSha256 = objectiveSha256,
            State = PlanningSequenceState.Armed,
            Revision = 1,
            ArmedAtUtc = armedAtUtc,
            UpdatedAtUtc = armedAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid CheckpointId { get; private set; }

    public string FingerprintSha256 { get; private set; }

    /// <summary>The lowercase SHA-256 of the run's immutable objective text at arming time.</summary>
    public string ObjectiveSha256 { get; private set; }

    public PlanningSequenceState State { get; private set; }

    /// <summary>One of <see cref="PlanningSequenceReason"/>; null while the sequence is Armed, preparing or awaiting a result.</summary>
    public string? ReasonCode { get; private set; }

    /// <summary>A bounded stable code that sharpens the reason (for example the refusing claim's own error code).</summary>
    public string? DetailCode { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset ArmedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? StopRequestedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    public bool IsAuthorityLive => PlanningSequencePolicy.IsAuthorityLive(State);

    /// <summary>Armed or awaiting a result, with a step to reserve: the next step's preparation intent was reserved.</summary>
    public void BeginPreparing(DateTimeOffset nowUtc)
    {
        if (State is not (PlanningSequenceState.Armed or PlanningSequenceState.AwaitingStepResult))
        {
            throw new InvalidOperationException($"A step cannot be reserved while the sequence is {State}.");
        }

        Advance(PlanningSequenceState.PreparingStep, nowUtc);
    }

    /// <summary>The reserved step's claim committed.</summary>
    public void MarkAwaitingResult(DateTimeOffset nowUtc)
    {
        if (State != PlanningSequenceState.PreparingStep)
        {
            throw new InvalidOperationException($"A claim cannot be linked while the sequence is {State}.");
        }

        Advance(PlanningSequenceState.AwaitingStepResult, nowUtc);
    }

    /// <summary>Stop after the current step, after the claim: the already claimed step may finish, no successor is allowed.</summary>
    public void BeginStopping(DateTimeOffset nowUtc)
    {
        if (State != PlanningSequenceState.AwaitingStepResult)
        {
            throw new InvalidOperationException($"A claimed step is not outstanding while the sequence is {State}.");
        }

        StopRequestedAtUtc = nowUtc;
        Advance(PlanningSequenceState.Stopping, nowUtc);
    }

    /// <summary>Ends the sequence in a terminal or attention state with a fixed reason. Never leaves an ended state.</summary>
    public void End(PlanningSequenceState endState, string reasonCode, string? detailCode, DateTimeOffset nowUtc)
    {
        if (!PlanningSequencePolicy.IsEnded(endState))
        {
            throw new ArgumentOutOfRangeException(nameof(endState), endState, "Not an ended state.");
        }

        if (!PlanningSequenceReason.IsKnown(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode), "Not a fixed planning sequence reason.");
        }

        if (!IsAuthorityLive)
        {
            throw new InvalidOperationException($"The sequence already ended as {State}.");
        }

        if (endState == PlanningSequenceState.Stopped && StopRequestedAtUtc is null)
        {
            StopRequestedAtUtc = nowUtc;
        }

        ReasonCode = reasonCode;
        DetailCode = PlanningSequenceReason.NormalizeDetail(detailCode);
        EndedAtUtc = nowUtc;
        Advance(endState, nowUtc);
    }

    private void Advance(PlanningSequenceState next, DateTimeOffset nowUtc)
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
