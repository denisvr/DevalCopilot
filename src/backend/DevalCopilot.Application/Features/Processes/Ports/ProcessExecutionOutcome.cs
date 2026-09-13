namespace DevalCopilot.Application.Features.Processes.Ports;

/// <summary>
/// How a single child-process execution ended. This describes only what happened to the OS
/// process itself — it is never a domain-level attempt or run outcome, which a later slice
/// derives from this together with other context.
/// </summary>
public enum ProcessExecutionOutcome
{
    /// <summary>The process ran to completion and reported an exit code, whether zero or not.</summary>
    Exited = 0,

    /// <summary>The process exceeded <see cref="ProcessExecutionRequest.Timeout"/> and its
    /// entire owned process tree was terminated by the adapter.</summary>
    TimedOut = 1,

    /// <summary>The caller's cancellation token was triggered and the entire owned process
    /// tree was terminated by the adapter.</summary>
    Cancelled = 2,
}
