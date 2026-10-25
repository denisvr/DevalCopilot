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

    /// <summary>ADR-0065: optional host-produced physical termination evidence, present only when the adapter's actual owned process
    /// instance was waited to completion successfully and its exit code obtained (normally, or within the existing bounded wait after a
    /// timeout or cancellation termination request). Absent for a launch or adapter fault, an unsuccessful post-termination wait and every
    /// adapter that does not own a real process. It never changes <see cref="Outcome"/> or <see cref="ExitCode"/>.</summary>
    public ProcessTerminationObservation? Termination { get; init; }
}
