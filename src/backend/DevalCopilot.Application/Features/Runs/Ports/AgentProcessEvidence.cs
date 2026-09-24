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
    /// <summary>True only for a process that ran to completion and exited with code zero.</summary>
    public bool IsCleanExit => Outcome == ProcessExecutionOutcome.Exited && ExitCode == 0;

    public static AgentProcessEvidence FromProcessExecutionResult(ProcessExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new AgentProcessEvidence(result.Outcome, result.ExitCode, result.Duration);
    }
}
