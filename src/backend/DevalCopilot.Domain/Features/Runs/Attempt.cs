namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One immutable claim of external work by the hosted supervisor — either the deterministic
/// simulated-agent sequence or a real child process. The attempt is committed before the
/// corresponding work runs, and never mutated by a later retry (a retry is a new attempt with
/// the next <see cref="AttemptNumber"/>).
/// </summary>
public sealed class Attempt
{
    private Attempt()
    {
    }

    public static Attempt Claim(Guid id, Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Simulated,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
        };
    }

    /// <summary>
    /// Claims a Process attempt, persisting its non-secret execution intent in the same call
    /// that starts it — the durable-intent invariant for process work: after a crash, the
    /// database describes what the interrupted attempt was actually going to run, not merely
    /// that it was a Process attempt.
    /// </summary>
    public static Attempt ClaimProcess(Guid id, Guid runId, int attemptNumber, ProcessExecutionIntent intent, DateTimeOffset claimedAtUtc)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.WorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.ApprovedRoot);

        var attempt = new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Process,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            ProcessExecutablePath = intent.ExecutablePath,
            ProcessWorkingDirectory = intent.WorkingDirectory,
            ProcessApprovedRoot = intent.ApprovedRoot,
            ProcessTimeout = intent.Timeout,
            ProcessMaxBytesPerStream = intent.MaxBytesPerStream,
            ProcessMaxTotalCapturedBytes = intent.MaxTotalCapturedBytes,
            // Defensively copied: intent.Arguments may be a caller-owned mutable list or
            // array, and a mutation to it after this call must never retroactively change
            // what this attempt durably committed to.
            ProcessArguments = intent.Arguments.ToArray(),
        };

        return attempt;
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public int AttemptNumber { get; private set; }

    public AttemptKind Kind { get; private set; }

    public AttemptStatus Status { get; private set; }

    public DateTimeOffset ClaimedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Absolute path of the executable a Process attempt committed to running.
    /// Only set when <see cref="Kind"/> is <see cref="AttemptKind.Process"/>.</summary>
    public string? ProcessExecutablePath { get; private set; }

    /// <summary>Literal argument values, in order. Empty for a Simulated attempt.</summary>
    public IReadOnlyList<string> ProcessArguments { get; private set; } = [];

    public string? ProcessWorkingDirectory { get; private set; }

    public string? ProcessApprovedRoot { get; private set; }

    public TimeSpan? ProcessTimeout { get; private set; }

    public int? ProcessMaxBytesPerStream { get; private set; }

    public int? ProcessMaxTotalCapturedBytes { get; private set; }

    /// <summary>How the child process actually ended. Only set once a Process attempt reaches
    /// <see cref="AttemptStatus.Completed"/> or <see cref="AttemptStatus.Failed"/> with a real
    /// result — never set for a Process attempt that failed without one (see
    /// <see cref="Fail"/>) or for a Simulated attempt.</summary>
    public ProcessOutcome? ProcessOutcome { get; private set; }

    /// <summary>Only set when <see cref="ProcessOutcome"/> is
    /// <see cref="Runs.ProcessOutcome.Exited"/> — a killed process's exit code is not a
    /// meaningful signal and is never recorded.</summary>
    public int? ProcessExitCode { get; private set; }

    /// <summary>
    /// When the external command was durably committed to running — set once, before the
    /// adapter is ever invoked, and never cleared. Distinguishes "claimed but not yet
    /// dispatched" (<see langword="null"/>, still eligible for dispatch) from "dispatched"
    /// (non-null: the external command has been invoked at most once for this attempt and
    /// must never be invoked again, even if it later never reaches a terminal result).
    /// </summary>
    public DateTimeOffset? ProcessDispatchedAtUtc { get; private set; }

    public void Complete(DateTimeOffset nowUtc)
    {
        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot complete an attempt that is {Status}.");
        }

        Status = AttemptStatus.Completed;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>
    /// Fails the attempt without recording any process result — the path for an adapter or
    /// request-reconstruction error that never produced a result to report at all. Never
    /// records exception text, output, or any other detail: only that the attempt did not
    /// succeed.
    /// </summary>
    public void Fail(DateTimeOffset nowUtc)
    {
        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot fail an attempt that is {Status}.");
        }

        Status = AttemptStatus.Failed;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>
    /// The single atomic completion transition for a Process attempt that produced a real
    /// process result: records the outcome and exit code and derives the resulting
    /// <see cref="AttemptStatus"/> in one step — a Process attempt is never observable holding
    /// a recorded outcome while still <see cref="AttemptStatus.Running"/>.
    /// </summary>
    public void CompleteProcess(ProcessOutcome outcome, int? exitCode, DateTimeOffset nowUtc)
    {
        if (Kind != AttemptKind.Process)
        {
            throw new InvalidOperationException("Only a Process attempt can record a process outcome.");
        }

        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot complete an attempt that is {Status}.");
        }

        var hasExitCode = exitCode.HasValue;
        if (outcome == Runs.ProcessOutcome.Exited && !hasExitCode)
        {
            throw new ArgumentException("An Exited outcome requires an exit code.", nameof(exitCode));
        }

        if (outcome != Runs.ProcessOutcome.Exited && hasExitCode)
        {
            throw new ArgumentException("Only an Exited outcome may carry an exit code.", nameof(exitCode));
        }

        ProcessOutcome = outcome;
        ProcessExitCode = exitCode;
        Status = outcome == Runs.ProcessOutcome.Exited && exitCode == 0
            ? AttemptStatus.Completed
            : AttemptStatus.Failed;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>
    /// The execution-start claim: durably committed in its own short transaction before the
    /// adapter is ever invoked for this attempt. Once set, this attempt is never eligible for
    /// dispatch again — the guard against invoking the external command more than once,
    /// independent of whether a terminal result is ever later recorded.
    /// </summary>
    public void MarkProcessDispatched(DateTimeOffset nowUtc)
    {
        if (Kind != AttemptKind.Process)
        {
            throw new InvalidOperationException("Only a Process attempt can be marked dispatched.");
        }

        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot mark an attempt dispatched that is {Status}.");
        }

        if (ProcessDispatchedAtUtc.HasValue)
        {
            throw new InvalidOperationException("This attempt was already marked dispatched.");
        }

        ProcessDispatchedAtUtc = nowUtc;
    }

    /// <summary>
    /// The restart-reconciliation transition: an application restart found this attempt still
    /// <see cref="AttemptStatus.Running"/> with no owning process left to observe it —
    /// regardless of whether it was ever dispatched, since either way nothing further may run
    /// for it in this host instance.
    /// </summary>
    public void Interrupt(DateTimeOffset nowUtc)
    {
        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot interrupt an attempt that is {Status}.");
        }

        Status = AttemptStatus.Interrupted;
        CompletedAtUtc = nowUtc;
    }
}
