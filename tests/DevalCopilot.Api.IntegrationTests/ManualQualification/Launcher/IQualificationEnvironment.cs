namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// Everything the session does to the outside world, in the order it does it. The production implementation composes the real host,
/// the authenticated HTTP routes, Git and the disposable storage; the offline regressions substitute a deterministic one to prove
/// the controls of <see cref="QualificationSession"/> without any provider.
/// </summary>
public interface IQualificationEnvironment : IAsyncDisposable
{
    /// <summary>Values that must never appear in the printed summary (the launch secret and every local path prefix).</summary>
    IReadOnlyList<string> ForbiddenFragments { get; }

    Task PrepareFixtureAsync(CancellationToken cancellationToken);

    SourceSnapshot SnapshotSource();

    Task StartHostAsync(CancellationToken cancellationToken);

    Task<TargetJudgement> ObserveTargetsAsync(TimeSpan bound, CancellationToken cancellationToken);

    Task<SetupFacts> RegisterAndPrepareAsync(CancellationToken cancellationToken);

    SourceSnapshot SnapshotWorkspace();

    /// <summary>The one POST of a stage. It is called at most once per role per session, only after the allowance was durably
    /// consumed, and any failure it cannot classify is reported as <see cref="SubmissionOutcome.Unknown"/>, never retried.</summary>
    Task<SubmissionResult> SubmitAsync(
        AllowanceRole role,
        Guid? proposalMessageId,
        CancellationToken cancellationToken);

    /// <summary>One bounded, read-only observation of the role's attempt. It never submits anything.</summary>
    Task<StageReading> ReadStageAsync(AllowanceRole role, CancellationToken cancellationToken);

    Task<ShutdownReport> StopHostAsync(CancellationToken cancellationToken);

    CleanupReport CleanUp();
}
