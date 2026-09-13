namespace DevalCopilot.Domain.Features.Runs;

public enum RunLifecycle
{
    Created = 0,
    Running = 1,
    Completed = 2,

    /// <summary>Its owning attempt ended without succeeding — either a real terminal failure
    /// (a Process attempt exited non-zero, timed out, or was cancelled) or, previously, the
    /// simulated flow's own failure path.</summary>
    Failed = 3,

    /// <summary>An application restart found this run's attempt still running with no owning
    /// process to observe. Terminal for this slice: a later intentional re-run is a new run,
    /// not an automatic retry.</summary>
    Interrupted = 4,
}
