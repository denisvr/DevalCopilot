namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One durable objective against an initial project, progressing through the bounded
/// deterministic simulated-agent-collaboration sequence for the walking-skeleton slice.
/// </summary>
public sealed class Run
{
    private Run()
    {
    }

    /// <summary>The fixed default reserved-time policy every new Run is assigned by
    /// <see cref="RecordIntent"/> — see the run-wide Agent invocation-time budget ADR. Unlike
    /// <see cref="MaximumAgentAttempts"/>'s historical backfill (ADR-0012), a historical Run
    /// predating that decision is never assigned this value retroactively: only rows constructed
    /// through this factory ever receive it, so a Run persisted directly (e.g. by an additive
    /// migration backfill) keeps <see cref="MaximumAgentInvocationTime"/> truthfully
    /// <see langword="null"/>.</summary>
    public static readonly TimeSpan DefaultMaximumAgentInvocationTime = TimeSpan.FromMinutes(120);

    public static Run RecordIntent(
        Guid id,
        Guid projectId,
        int executionNumber,
        string objective,
        DateTimeOffset nowUtc,
        int maximumReviewCorrectionAttempts = 2,
        int maximumAgentAttempts = 16,
        TimeSpan? maximumAgentInvocationTime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);

        if (executionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(executionNumber));
        }

        if (maximumReviewCorrectionAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumReviewCorrectionAttempts));
        }

        if (maximumAgentAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAgentAttempts));
        }

        if (maximumAgentInvocationTime is { } requestedInvocationTime && requestedInvocationTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAgentInvocationTime), requestedInvocationTime, "Must be a positive, bounded reservation ceiling.");
        }

        return new Run
        {
            Id = id,
            ProjectId = projectId,
            ExecutionNumber = executionNumber,
            Objective = objective,
            Lifecycle = RunLifecycle.Created,
            Stage = RunStage.Intake,
            ActiveParticipantKind = ParticipantKind.None,
            CreatedAtUtc = nowUtc,
            LastAdvancedAtUtc = nowUtc,
            AccumulatedAutonomousSeconds = 0,
            MaximumReviewCorrectionAttempts = maximumReviewCorrectionAttempts,
            MaximumAgentAttempts = maximumAgentAttempts,
            MaximumAgentInvocationTime = maximumAgentInvocationTime ?? DefaultMaximumAgentInvocationTime,
        };
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public int ExecutionNumber { get; private set; }

    public string Objective { get; private set; } = string.Empty;

    public RunLifecycle Lifecycle { get; private set; }

    public RunStage Stage { get; private set; }

    public ParticipantKind ActiveParticipantKind { get; private set; }

    public AgentRole? ActiveAgentRole { get; private set; }

    public AgentProvider? ActiveAgentProvider { get; private set; }

    public ParticipantIdentity ActiveParticipant =>
        ParticipantIdentity.FromParts(ActiveParticipantKind, ActiveAgentRole, ActiveAgentProvider);

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastAdvancedAtUtc { get; private set; }

    /// <summary>
    /// Total time accumulated while <see cref="Lifecycle"/> was <see cref="RunLifecycle.Running"/>.
    /// Excludes intent-recorded (not yet claimed) and terminal time.
    /// </summary>
    public double AccumulatedAutonomousSeconds { get; private set; }

    /// <summary>
    /// Maximum number of claimed review-correction attempts for this run. Claims consume this
    /// immutable-per-run policy value even when the provider later fails or the attempt is
    /// interrupted.
    /// </summary>
    public int MaximumReviewCorrectionAttempts { get; private set; }

    /// <summary>
    /// Maximum number of claimed Agent attempts for this run, across every role, provider,
    /// dispatch outcome, and interruption. Every claimed Agent attempt — regardless of the six
    /// distinct claim paths that create one — consumes exactly one permanent slot of this
    /// immutable-per-run policy value, even when the provider later fails or the attempt is
    /// interrupted. Simulated and Process attempts never consume this budget. Unlike
    /// <see cref="MaximumReviewCorrectionAttempts"/>, exhaustion of this budget has no human
    /// override: it is a hard ceiling for the run.
    /// </summary>
    public int MaximumAgentAttempts { get; private set; }

    /// <summary>
    /// Maximum total reserved Agent invocation time for this run, independent of and enforced
    /// alongside <see cref="MaximumAgentAttempts"/> — see the run-wide Agent invocation-time
    /// budget ADR. Every claimed Agent attempt permanently reserves its own configured
    /// <see cref="Attempt.AgentTimeout"/>, whether later dispatched, failed, or interrupted;
    /// Simulated and Process attempts never consume it. <see langword="null"/> means this Run has
    /// no time-budget policy at all — truthfully distinguishing a historical Run that predates
    /// this decision (never assigned a fabricated value) from a Run that carries a real, bounded
    /// policy. Unlike <see cref="MaximumAgentAttempts"/>, no historical Run's value is ever
    /// migrated or backfilled: only <see cref="RecordIntent"/> ever assigns a non-null value, to a
    /// newly created Run.
    /// </summary>
    public TimeSpan? MaximumAgentInvocationTime { get; private set; }

    /// <summary>
    /// The hosted supervisor claims recorded intent and starts the simulated attempt.
    /// This is the transition that must happen outside the command that recorded intent.
    /// </summary>
    public void Claim(DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Created)
        {
            throw new InvalidOperationException($"Cannot claim a run whose lifecycle is {Lifecycle}.");
        }

        Lifecycle = RunLifecycle.Running;
        SetActiveParticipant(ParticipantIdentity.ForOrchestrator());
        LastAdvancedAtUtc = nowUtc;
    }

    public void AdvanceStage(RunStage nextStage, ParticipantIdentity participant, DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Running)
        {
            throw new InvalidOperationException($"Cannot advance a run whose lifecycle is {Lifecycle}.");
        }

        if (nextStage <= Stage)
        {
            throw new InvalidOperationException(
                $"Stage must advance forward: cannot move from {Stage} to {nextStage}.");
        }

        AccumulateAutonomousTime(nowUtc);
        Stage = nextStage;
        SetActiveParticipant(participant);
        LastAdvancedAtUtc = nowUtc;
    }

    public void Complete(DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Running)
        {
            throw new InvalidOperationException($"Cannot complete a run whose lifecycle is {Lifecycle}.");
        }

        AccumulateAutonomousTime(nowUtc);
        Lifecycle = RunLifecycle.Completed;
        Stage = RunStage.Completed;
        SetActiveParticipant(ParticipantIdentity.None());
        LastAdvancedAtUtc = nowUtc;
    }

    /// <summary>
    /// Its owning attempt ended without succeeding. <see cref="Stage"/> is left as-is: a
    /// failure does not represent forward progress to a later stage.
    /// </summary>
    public void Fail(DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Running)
        {
            throw new InvalidOperationException($"Cannot fail a run whose lifecycle is {Lifecycle}.");
        }

        AccumulateAutonomousTime(nowUtc);
        Lifecycle = RunLifecycle.Failed;
        SetActiveParticipant(ParticipantIdentity.None());
        LastAdvancedAtUtc = nowUtc;
    }

    /// <summary>
    /// The restart-reconciliation transition, applied atomically alongside the owning
    /// attempt's own <see cref="Attempt.Interrupt"/>. <see cref="Stage"/> is left as-is —
    /// this is not forward progress, and a later intentional re-run is a new run.
    /// </summary>
    public void MarkInterrupted(DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Running)
        {
            throw new InvalidOperationException($"Cannot interrupt a run whose lifecycle is {Lifecycle}.");
        }

        AccumulateAutonomousTime(nowUtc);
        Lifecycle = RunLifecycle.Interrupted;
        SetActiveParticipant(ParticipantIdentity.None());
        LastAdvancedAtUtc = nowUtc;
    }

    private void AccumulateAutonomousTime(DateTimeOffset nowUtc)
    {
        var elapsed = (nowUtc - LastAdvancedAtUtc).TotalSeconds;

        if (elapsed > 0)
        {
            AccumulatedAutonomousSeconds += elapsed;
        }
    }

    private void SetActiveParticipant(ParticipantIdentity participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ActiveParticipantKind = participant.Kind;
        ActiveAgentRole = participant.Role;
        ActiveAgentProvider = participant.Provider;
    }
}
