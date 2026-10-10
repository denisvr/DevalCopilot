using DevalCopilot.Api.IntegrationTests.ManualQualification;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>A deterministic stand-in for the outside world, used only to exercise the launcher controls. It records every call in
/// order and lets a test script each answer; it is never evidence of a provider result.</summary>
public sealed class ScriptedEnvironment : IQualificationEnvironment
{
    public const string WorkspaceReference = "refs/heads/workspace";

    private readonly object _gate = new();
    private readonly List<string> _calls = [];

    public Guid PlannerAttemptId { get; } = Guid.NewGuid();

    public Guid ProposalMessageId { get; } = Guid.NewGuid();

    public Guid ReviewerAttemptId { get; } = Guid.NewGuid();

    public Guid ReplyMessageId { get; } = Guid.NewGuid();

    public IReadOnlyList<string> ForbiddenFragments { get; set; } = [];

    public TargetJudgement Targets { get; set; } = new(true, [new TargetReport("codex", true, "Real", "NodeScript", "1.0.0")]);

    public Func<AllowanceRole, Guid?, Task<SubmissionResult>>? OnSubmit { get; set; }

    public Func<AllowanceRole, int, StageReading>? OnRead { get; set; }

    public Func<int, SourceSnapshot> SourceAt { get; set; } = _ => QualificationReadings.Snapshot();

    public Func<int, SourceSnapshot> WorkspaceAt { get; set; } = _ => QualificationReadings.Snapshot("w");

    public ShutdownReport Shutdown { get; set; } = new(true, true);

    public Func<CleanupReport> Cleanup { get; set; } = () => CleanupReport.RemovedRoot;

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public int Count(string call) => Calls.Count(entry => entry == call);

    public Task PrepareFixtureAsync(CancellationToken cancellationToken)
    {
        Note("prepare");
        return Task.CompletedTask;
    }

    public SourceSnapshot SnapshotSource()
    {
        Note("snapshot.source");
        return SourceAt(Count("snapshot.source"));
    }

    public SourceSnapshot SnapshotWorkspace()
    {
        Note("snapshot.workspace");
        return WorkspaceAt(Count("snapshot.workspace"));
    }

    public Task StartHostAsync(CancellationToken cancellationToken)
    {
        Note("start");
        return Task.CompletedTask;
    }

    public Task<TargetJudgement> ObserveTargetsAsync(TimeSpan bound, CancellationToken cancellationToken)
    {
        Note("observe");
        return Task.FromResult(Targets);
    }

    public Task<SetupFacts> RegisterAndPrepareAsync(CancellationToken cancellationToken)
    {
        Note("setup");
        return Task.FromResult(new SetupFacts(Guid.NewGuid(), Guid.NewGuid(), WorkspaceReference));
    }

    public Task<SubmissionResult> SubmitAsync(AllowanceRole role, Guid? proposalMessageId, CancellationToken cancellationToken)
    {
        Note($"submit.{role}");
        return OnSubmit is not null
            ? OnSubmit(role, proposalMessageId)
            : Task.FromResult(SubmissionResult.Accepted(role == AllowanceRole.Planner ? PlannerAttemptId : ReviewerAttemptId));
    }

    public Task<StageReading> ReadStageAsync(AllowanceRole role, CancellationToken cancellationToken)
    {
        Note($"read.{role}");
        var reading = OnRead?.Invoke(role, Count($"read.{role}"))
            ?? (role == AllowanceRole.Planner
                ? QualificationReadings.Planner(PlannerAttemptId, ProposalMessageId)
                : QualificationReadings.Reviewer(ReviewerAttemptId, ReplyMessageId, ProposalMessageId));
        return Task.FromResult(reading);
    }

    public Task<ShutdownReport> StopHostAsync(CancellationToken cancellationToken)
    {
        Note("stop");
        return Task.FromResult(Shutdown);
    }

    public CleanupReport CleanUp()
    {
        Note("cleanup");
        return Cleanup();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Note(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }
}
