namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// Puts the whole-entry deadline between a still-running session and its environment. Once the deadline elapsed, a detached
/// continuation of the session can no longer reach any effect: preparation, host start, readiness, registration, submission, reads
/// and snapshots stop with a cancellation, a stop of the host is reported as unproven and does nothing, the removal of the owned root
/// is refused and reports its reason, and disposal leaves the host and the root exactly as they are. Before the deadline every call
/// is forwarded unchanged. This claims no pre-emption of a call already running and no shutdown of anything.
/// </summary>
public sealed class DeadlineGatedEnvironment(IQualificationEnvironment inner, EntryDeadline deadline) : IQualificationEnvironment
{
    public IReadOnlyList<string> ForbiddenFragments => inner.ForbiddenFragments;

    public ProcessAdmissionFacts AdmissionFacts => inner.AdmissionFacts;

    public Task PrepareFixtureAsync(CancellationToken cancellationToken) =>
        Gate(() => inner.PrepareFixtureAsync(cancellationToken));

    public SourceSnapshot SnapshotSource()
    {
        ThrowIfExpired();
        return inner.SnapshotSource();
    }

    public Task StartHostAsync(CancellationToken cancellationToken) =>
        Gate(() => inner.StartHostAsync(cancellationToken));

    public Task<TargetJudgement> ObserveTargetsAsync(TimeSpan bound, CancellationToken cancellationToken) =>
        Gate(() => inner.ObserveTargetsAsync(bound, cancellationToken));

    public Task<SetupFacts> RegisterAndPrepareAsync(CancellationToken cancellationToken) =>
        Gate(() => inner.RegisterAndPrepareAsync(cancellationToken));

    public SourceSnapshot SnapshotWorkspace()
    {
        ThrowIfExpired();
        return inner.SnapshotWorkspace();
    }

    public Task<SubmissionResult> SubmitAsync(AllowanceRole role, Guid? proposalMessageId, CancellationToken cancellationToken) =>
        Gate(() => inner.SubmitAsync(role, proposalMessageId, cancellationToken));

    public Task<StageReading> ReadStageAsync(AllowanceRole role, CancellationToken cancellationToken) =>
        Gate(() => inner.ReadStageAsync(role, cancellationToken));

    public Task<ShutdownReport> StopHostAsync(CancellationToken cancellationToken) =>
        deadline.IsExpired ? Task.FromResult(ShutdownReport.Unproven) : inner.StopHostAsync(cancellationToken);

    public CleanupReport CleanUp() =>
        deadline.IsExpired ? CleanupReport.Preserved("OuterDeadlineExceeded") : inner.CleanUp();

    public ValueTask DisposeAsync() =>
        deadline.IsExpired ? ValueTask.CompletedTask : inner.DisposeAsync();

    private void ThrowIfExpired()
    {
        if (deadline.IsExpired)
        {
            throw new OperationCanceledException("The entry deadline elapsed.");
        }
    }

    private Task<T> Gate<T>(Func<Task<T>> call)
    {
        ThrowIfExpired();
        return call();
    }

    private Task Gate(Func<Task> call)
    {
        ThrowIfExpired();
        return call();
    }
}
