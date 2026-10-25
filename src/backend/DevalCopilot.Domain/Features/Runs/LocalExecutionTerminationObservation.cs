namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// ADR-0065: the immutable physical termination observation of one dispatched local execution operation, recorded by the witness's
/// original host from its actual returned process result, atomically with the ordinary terminal result. It retains the matching returned
/// process outcome, semantic exit code and process duration (which agree with the operation's own stored tuple where that is present) next
/// to the distinct physical fact: the exit code the operating system reported for the terminated process and the monotonic elapsed time at
/// the successful wait. The recording time is the host's recording time, not an exact process-exit timestamp.
///
/// <para>
/// It is physical evidence about the invoked process only, never every descendant, and it never changes the result: a timed-out or
/// cancelled process keeps its TimedOut or Cancelled outcome and its null semantic exit code, and nothing here is a successful execution,
/// a verification pass, a review or an approval. It is append-only: no update, replacement, backfill or delete.
/// </para>
/// </summary>
public sealed class LocalExecutionTerminationObservation
{
    public const int CurrentVersion = 1;

    private LocalExecutionTerminationObservation()
    {
    }

    public static LocalExecutionTerminationObservation Record(
        Guid id,
        LocalExecutionDispatchWitness witness,
        ProcessOutcome processOutcome,
        int? semanticExitCode,
        TimeSpan processDuration,
        int physicalExitCode,
        TimeSpan observedElapsed,
        DateTimeOffset recordedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An observation requires a durable identifier.", nameof(id));
        }

        if (!IsCoherent(processOutcome, semanticExitCode, processDuration, physicalExitCode, observedElapsed))
        {
            throw new ArgumentException("The physical termination fact contradicts the returned process result.");
        }

        if (recordedAtUtc == default || recordedAtUtc < witness.DispatchedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(recordedAtUtc), "An observation cannot precede its dispatch.");
        }

        return new LocalExecutionTerminationObservation
        {
            Id = id,
            Version = CurrentVersion,
            WitnessId = witness.Id,
            ProcessOutcome = processOutcome,
            SemanticExitCode = semanticExitCode,
            ProcessDuration = processDuration,
            PhysicalExitCode = physicalExitCode,
            ObservedElapsed = observedElapsed,
            RecordedAtUtc = recordedAtUtc,
        };
    }

    /// <summary>Whether the physical fact is consistent with the returned process result: defined outcome, an exit code exactly for
    /// Exited (and equal to the physical one), nonnegative durations and an observed elapsed time no greater than the process duration.
    /// Physical termination of a timed-out or cancelled process is valid and keeps a null semantic exit code.</summary>
    public static bool IsCoherent(
        ProcessOutcome processOutcome, int? semanticExitCode, TimeSpan processDuration, int physicalExitCode, TimeSpan observedElapsed) =>
        Enum.IsDefined(processOutcome)
        && (processOutcome == ProcessOutcome.Exited) == semanticExitCode.HasValue
        && (semanticExitCode is not { } semantic || semantic == physicalExitCode)
        && processDuration >= TimeSpan.Zero
        && observedElapsed >= TimeSpan.Zero
        && observedElapsed <= processDuration;

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid WitnessId { get; private set; }

    public ProcessOutcome ProcessOutcome { get; private set; }

    /// <summary>The semantic exit code of the returned result; set only for Exited.</summary>
    public int? SemanticExitCode { get; private set; }

    public TimeSpan ProcessDuration { get; private set; }

    /// <summary>The exit code the operating system reported for the terminated process.</summary>
    public int PhysicalExitCode { get; private set; }

    /// <summary>The nonnegative monotonic elapsed time at the successful wait, never greater than <see cref="ProcessDuration"/>.</summary>
    public TimeSpan ObservedElapsed { get; private set; }

    /// <summary>The host's recording time; not an exact process-exit timestamp.</summary>
    public DateTimeOffset RecordedAtUtc { get; private set; }
}
