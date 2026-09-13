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

    public static Run RecordIntent(Guid id, Guid projectId, int executionNumber, string objective, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);

        if (executionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(executionNumber));
        }

        return new Run
        {
            Id = id,
            ProjectId = projectId,
            ExecutionNumber = executionNumber,
            Objective = objective,
            Lifecycle = RunLifecycle.Created,
            Stage = RunStage.Intake,
            ActiveParticipant = ParticipantKind.None,
            CreatedAtUtc = nowUtc,
            LastAdvancedAtUtc = nowUtc,
            AccumulatedAutonomousSeconds = 0,
        };
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public int ExecutionNumber { get; private set; }

    public string Objective { get; private set; } = string.Empty;

    public RunLifecycle Lifecycle { get; private set; }

    public RunStage Stage { get; private set; }

    public ParticipantKind ActiveParticipant { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastAdvancedAtUtc { get; private set; }

    /// <summary>
    /// Total time accumulated while <see cref="Lifecycle"/> was <see cref="RunLifecycle.Running"/>.
    /// Excludes intent-recorded (not yet claimed) and terminal time.
    /// </summary>
    public double AccumulatedAutonomousSeconds { get; private set; }

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
        ActiveParticipant = ParticipantKind.Orchestrator;
        LastAdvancedAtUtc = nowUtc;
    }

    public void AdvanceStage(RunStage nextStage, ParticipantKind participant, DateTimeOffset nowUtc)
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
        ActiveParticipant = participant;
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
        ActiveParticipant = ParticipantKind.None;
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
        ActiveParticipant = ParticipantKind.None;
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
        ActiveParticipant = ParticipantKind.None;
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
}
