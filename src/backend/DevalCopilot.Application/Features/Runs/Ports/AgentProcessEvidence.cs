using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Provider-neutral, host-measured evidence of how one Agent invocation's child process ended,
/// shared by every Agent adapter port regardless of provider. It is derived only from a real
/// <see cref="ProcessExecutionResult"/> — an adapter that never obtained one (the launch target,
/// manifest, or scratch preparation failed before any process started) reports no evidence at
/// all rather than an invented value. It never carries a path, argument, environment value,
/// output, session identifier, or credential, and it is never a semantic classification of the
/// provider's response.
/// </summary>
public sealed record AgentProcessEvidence(ProcessExecutionOutcome Outcome, int? ExitCode, TimeSpan Duration)
{
    /// <summary>True only for a process that ran to completion and exited with code zero. ADR-0065: a physical termination observation
    /// is a separate fact and never makes a timed-out or cancelled process a clean exit.</summary>
    public bool IsCleanExit => Outcome == ProcessExecutionOutcome.Exited && ExitCode == 0;

    /// <summary>ADR-0065: the optional host-produced physical termination fact of the actual owned process, forwarded unchanged from the
    /// process result. It is distinct from <see cref="Outcome"/> and <see cref="ExitCode"/> and is never read as success, a verification
    /// pass, review or approval; the Domain process-evidence invariants do not consume it.</summary>
    public ProcessTerminationObservation? Termination { get; init; }

    public static AgentProcessEvidence FromProcessExecutionResult(ProcessExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new AgentProcessEvidence(result.Outcome, result.ExitCode, result.Duration) { Termination = result.Termination };
    }
}
