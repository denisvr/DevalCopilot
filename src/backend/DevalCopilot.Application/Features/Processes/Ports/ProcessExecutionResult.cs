namespace DevalCopilot.Application.Features.Processes.Ports;

/// <summary>
/// The outcome of one child-process execution. Captured output is UTF-8 text bounded by the
/// request's capture caps and held only in memory — this foundation slice keeps no durable
/// artifact of process output; that is deferred to the slice that wires this adapter into a
/// persisted attempt.
/// </summary>
public sealed record ProcessExecutionResult
{
    public required ProcessExecutionOutcome Outcome { get; init; }

    /// <summary>Only populated when <see cref="Outcome"/> is
    /// <see cref="ProcessExecutionOutcome.Exited"/> — a killed process's exit code is not a
    /// meaningful signal and is never reported.</summary>
    public int? ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    /// <summary>True when captured stdout was cut off by either the per-stream or the
    /// combined capture cap. The discarded remainder is never retained anywhere.</summary>
    public required bool StandardOutputTruncated { get; init; }

    public required string StandardError { get; init; }

    /// <summary>True when captured stderr was cut off by either the per-stream or the
    /// combined capture cap. The discarded remainder is never retained anywhere.</summary>
    public required bool StandardErrorTruncated { get; init; }

    public required TimeSpan Duration { get; init; }
}
