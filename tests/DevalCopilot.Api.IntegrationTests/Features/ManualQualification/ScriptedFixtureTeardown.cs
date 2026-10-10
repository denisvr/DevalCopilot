using DevalCopilot.Api.IntegrationTests.ManualQualification;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// Offline fixture teardown for the one rehearsal whose process observation is scripted. It acts only on the exact root the
/// environment created, with the token it generated then, never on a discovered or read-back one, and only when the fixture's
/// own shutdown is established from real facts: the production host stopped within its bound and both provider doubles were
/// measured to have exited. A scripted process table proves nothing about a native process, so a native observation never reaches
/// this teardown, and neither does a root that was already removed. It is not part of the launcher and is not a janitor.
/// </summary>
public static class ScriptedFixtureTeardown
{
    public static CleanupReport? TryRemove(
        OwnedRootReference? root,
        QualificationReport report,
        bool scriptedObservation,
        Func<string, string, CleanupReport> remove)
    {
        if (root is null || !scriptedObservation)
        {
            return null;
        }

        var hostStopped = report.Shutdown is { HostStopped: true };
        var doublesExited = report.Planner is { ProcessOutcome: "Exited" } && report.Reviewer is { ProcessOutcome: "Exited" };
        var stillPreserved = report.Cleanup is { Removed: false };
        return hostStopped && doublesExited && stillPreserved ? remove(root.Path, root.OwnerToken) : null;
    }
}
