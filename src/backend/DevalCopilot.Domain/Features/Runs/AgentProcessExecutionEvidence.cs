namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The host-measured facts about how one Agent attempt's provider child process ended: its
/// <see cref="ProcessOutcome"/>, an exit code only for <see cref="ProcessOutcome.Exited"/>, and a
/// non-negative duration measured by the host rather than reported by the provider. This is
/// process-level execution evidence only — it is never a semantic classification of the attempt,
/// which remains <see cref="AgentOutcome"/>. Mirrors the shape rules of
/// <see cref="Attempt.CompleteProcess"/> for Process attempts. An attempt whose provider process
/// never produced a result (it was never dispatched, or the invocation failed before a process
/// started) has no instance of this type at all — absence is never replaced by an invented value.
/// </summary>
public sealed record AgentProcessExecutionEvidence
{
    private AgentProcessExecutionEvidence(ProcessOutcome outcome, int? exitCode, TimeSpan duration)
    {
        Outcome = outcome;
        ExitCode = exitCode;
        Duration = duration;
    }

    public ProcessOutcome Outcome { get; }

    /// <summary>Only set when <see cref="Outcome"/> is <see cref="ProcessOutcome.Exited"/>.</summary>
    public int? ExitCode { get; }

    /// <summary>Non-negative wall-clock duration measured by the host around the child process.</summary>
    public TimeSpan Duration { get; }

    /// <summary>True only for a process that ran to completion and exited with code zero.</summary>
    public bool IsCleanExit => Outcome == ProcessOutcome.Exited && ExitCode == 0;

    /// <summary>Creates validated evidence, throwing <see cref="ArgumentException"/> for any shape
    /// <see cref="Validate"/> rejects.</summary>
    public static AgentProcessExecutionEvidence Create(ProcessOutcome outcome, int? exitCode, TimeSpan duration)
    {
        var violation = Validate(outcome, exitCode, duration);
        if (violation is not null)
        {
            throw new ArgumentException($"Invalid agent process evidence: {violation}.", nameof(outcome));
        }

        return new AgentProcessExecutionEvidence(outcome, exitCode, duration);
    }

    /// <summary>Returns the first shape violation, or <see langword="null"/> when the shape is valid.</summary>
    public static AgentProcessEvidenceViolation? Validate(ProcessOutcome outcome, int? exitCode, TimeSpan duration)
    {
        if (!Enum.IsDefined(outcome))
        {
            return AgentProcessEvidenceViolation.UndefinedOutcome;
        }

        if (outcome == ProcessOutcome.Exited && !exitCode.HasValue)
        {
            return AgentProcessEvidenceViolation.ExitedWithoutExitCode;
        }

        if (outcome != ProcessOutcome.Exited && exitCode.HasValue)
        {
            return AgentProcessEvidenceViolation.ExitCodeWithoutExited;
        }

        if (duration < TimeSpan.Zero)
        {
            return AgentProcessEvidenceViolation.NegativeDuration;
        }

        return null;
    }
}
