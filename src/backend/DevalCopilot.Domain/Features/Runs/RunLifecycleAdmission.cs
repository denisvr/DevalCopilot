namespace DevalCopilot.Domain.Features.Runs;

/// <summary>Which lifecycles permit a project to record another intent. Only a recognized terminal lifecycle does; Created, Running,
/// and any unrecognized value block. Abandoned is terminal only for coherent abandonment facts (ADR-0031), so it is never admitted by
/// the lifecycle alone.</summary>
public static class RunLifecycleAdmission
{
    public static bool PermitsNewIntent(RunLifecycle lifecycle) => PermitsNewIntent(lifecycle, coherentAbandonment: false);

    /// <param name="coherentAbandonment">True only when <see cref="RunAbandonmentPolicy.IsCoherent"/> accepted this run's facts; it
    /// is ignored for every other lifecycle.</param>
    public static bool PermitsNewIntent(RunLifecycle lifecycle, bool coherentAbandonment) =>
        lifecycle is RunLifecycle.Completed or RunLifecycle.Failed or RunLifecycle.Interrupted
        || (lifecycle == RunLifecycle.Abandoned && coherentAbandonment);
}
