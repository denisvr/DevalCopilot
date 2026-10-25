namespace DevalCopilot.Application.Features.Processes.Ports;

/// <summary>
/// ADR-0065: the host-produced physical fact that the one owned <see cref="System.Diagnostics.Process"/> instance of an execution had
/// terminated and its exit code was obtained: that code (the one the operating system reported, which for a process the adapter killed
/// is not a meaningful semantic exit code) and the monotonic elapsed time at that observation. The adapter
/// produces it only after <c>WaitForExitAsync</c> of that same instance completed successfully, either normally or within the existing
/// bounded wait after its timeout or cancellation termination request. It is never inferred from an outcome, a deadline, a missing
/// result, a heartbeat or any assertion, and it never changes <see cref="ProcessExecutionResult.Outcome"/> or
/// <see cref="ProcessExecutionResult.ExitCode"/>: a timed-out or cancelled process stays TimedOut or Cancelled with no semantic exit
/// code. It proves only the invoked process, never every descendant.
/// </summary>
public sealed record ProcessTerminationObservation
{
    public ProcessTerminationObservation(int physicalExitCode, TimeSpan observedElapsed)
    {
        if (observedElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(observedElapsed), "The observed elapsed time cannot be negative.");
        }

        PhysicalExitCode = physicalExitCode;
        ObservedElapsed = observedElapsed;
    }

    /// <summary>The exit code the operating system reported for the terminated process.</summary>
    public int PhysicalExitCode { get; }

    /// <summary>The nonnegative monotonic elapsed time at the successful wait, never later than the result's own duration.</summary>
    public TimeSpan ObservedElapsed { get; }
}
