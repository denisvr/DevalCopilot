namespace DevalCopilot.Domain.Features.Runs;

/// <summary>Which lifecycles permit a project to record another intent. Only a recognized terminal
/// lifecycle does; Created, Running, and any unrecognized value block.</summary>
public static class RunLifecycleAdmission
{
    public static bool PermitsNewIntent(RunLifecycle lifecycle) =>
        lifecycle is RunLifecycle.Completed or RunLifecycle.Failed or RunLifecycle.Interrupted;
}
