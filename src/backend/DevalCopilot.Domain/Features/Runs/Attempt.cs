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

    /// <summary>
    /// Claims an Agent attempt — a durable, real invocation of an external agent provider —
    /// persisting its complete non-secret intent in the same call that starts it. This slice
    /// accepts only Codex, the Planner role, protocol 1.0, and an expected Proposal: those four
    /// facts are fixed by this factory, not caller-supplied, so no caller can construct any other
    /// combination. Workspace/checkpoint identity are passed as bare identifiers — cross-checking
    /// that the checkpoint actually belongs to that workspace, and that both belong to the
    /// intended project, is the calling Application handler's responsibility (it already loads
    /// both entities), keeping this Domain feature independent of the Projects feature.
    /// </summary>
    public static Attempt ClaimAgent(
        Guid id,
        Guid runId,
        int attemptNumber,
        Guid gitWorkspaceId,
        Guid gitCheckpointId,
        string checkpointFingerprintSha256,
        Guid contextManifestArtifactId,
        TimeSpan timeout,
        int maxBytesPerStream,
        int maxTotalCapturedBytes,
        DateTimeOffset claimedAtUtc)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        if (gitWorkspaceId == Guid.Empty)
        {
            throw new ArgumentException("A Git workspace identity is required.", nameof(gitWorkspaceId));
        }

        if (gitCheckpointId == Guid.Empty)
        {
            throw new ArgumentException("A Git checkpoint identity is required.", nameof(gitCheckpointId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointFingerprintSha256);

        if (contextManifestArtifactId == Guid.Empty)
        {
            throw new ArgumentException("A context manifest artifact identity is required.", nameof(contextManifestArtifactId));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Must be a positive, bounded timeout.");
        }

        if (maxBytesPerStream < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytesPerStream));
        }

        if (maxTotalCapturedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalCapturedBytes));
        }

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Agent,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            AgentProvider = Runs.AgentProvider.Codex,
            AgentRole = Runs.AgentRole.Planner,
            AgentProtocolVersion = CollaborationMessage.ProtocolVersionOne,
            AgentExpectedMessageType = CollaborationMessageType.Proposal,
            AgentResponseContract = Runs.AgentResponseContract.Proposal,
            AgentGitWorkspaceId = gitWorkspaceId,
            AgentGitCheckpointId = gitCheckpointId,
            AgentCheckpointFingerprintSha256 = checkpointFingerprintSha256,
            AgentContextManifestArtifactId = contextManifestArtifactId,
            AgentTimeout = timeout,
            AgentMaxBytesPerStream = maxBytesPerStream,
            AgentMaxTotalCapturedBytes = maxTotalCapturedBytes,
        };
    }

    /// <summary>
    /// Claims a Claude Code critical-review Agent attempt — a durable, real invocation reviewing
    /// one exact, already-recorded Codex Proposal. This slice accepts only ClaudeCode, the
    /// CriticalReviewer role, protocol 1.0, and the <see cref="Runs.AgentResponseContract.CriticalReview"/>
    /// contract: those four facts are fixed by this factory, not caller-supplied, so no caller can
    /// construct any other combination — a dedicated factory per attempt shape, never a shared,
    /// loosely validated bag of nullable arguments with <see cref="ClaimAgent"/>. Unlike planning,
    /// <paramref name="inputCollaborationMessageId"/> is required and immutable: a review attempt
    /// is meaningless without the exact Proposal it reviews. Workspace/checkpoint/message identity
    /// are passed as bare identifiers — cross-checking that the checkpoint belongs to the
    /// workspace, that the message belongs to the run, and that both belong to the intended
    /// project, is the calling Application handler's responsibility (it already loads all three
    /// entities), keeping this Domain feature independent of the Projects feature.
    /// </summary>
    public static Attempt ClaimAgentCriticalReview(
        Guid id,
        Guid runId,
        int attemptNumber,
        Guid gitWorkspaceId,
        Guid gitCheckpointId,
        string checkpointFingerprintSha256,
        Guid inputCollaborationMessageId,
        Guid contextManifestArtifactId,
        TimeSpan timeout,
        int maxBytesPerStream,
        int maxTotalCapturedBytes,
        DateTimeOffset claimedAtUtc)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        if (gitWorkspaceId == Guid.Empty)
        {
            throw new ArgumentException("A Git workspace identity is required.", nameof(gitWorkspaceId));
        }

        if (gitCheckpointId == Guid.Empty)
        {
            throw new ArgumentException("A Git checkpoint identity is required.", nameof(gitCheckpointId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointFingerprintSha256);

        if (inputCollaborationMessageId == Guid.Empty)
        {
            throw new ArgumentException(
                "A critical-review attempt requires the exact input collaboration message it reviews.",
                nameof(inputCollaborationMessageId));
        }

        if (contextManifestArtifactId == Guid.Empty)
        {
            throw new ArgumentException("A context manifest artifact identity is required.", nameof(contextManifestArtifactId));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Must be a positive, bounded timeout.");
        }

        if (maxBytesPerStream < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytesPerStream));
        }

        if (maxTotalCapturedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalCapturedBytes));
        }

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Agent,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            AgentProvider = Runs.AgentProvider.ClaudeCode,
            AgentRole = Runs.AgentRole.CriticalReviewer,
            AgentProtocolVersion = CollaborationMessage.ProtocolVersionOne,
            // A critical-review attempt's own claimed intent is still, in protocol terms, "expect
            // to produce one Proposal-replying message" — the actual Acceptance/Challenge union
            // this really returns is represented by AgentResponseContract below, never by this
            // field, and never by encoding that union as null.
            AgentExpectedMessageType = CollaborationMessageType.Proposal,
            AgentResponseContract = Runs.AgentResponseContract.CriticalReview,
            AgentInputCollaborationMessageId = inputCollaborationMessageId,
            AgentGitWorkspaceId = gitWorkspaceId,
            AgentGitCheckpointId = gitCheckpointId,
            AgentCheckpointFingerprintSha256 = checkpointFingerprintSha256,
            AgentContextManifestArtifactId = contextManifestArtifactId,
            AgentTimeout = timeout,
            AgentMaxBytesPerStream = maxBytesPerStream,
            AgentMaxTotalCapturedBytes = maxTotalCapturedBytes,
        };
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

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. Fixed to
    /// <see cref="Runs.AgentProvider.Codex"/> in this slice.</summary>
    public AgentProvider? AgentProvider { get; private set; }

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. Fixed to
    /// <see cref="Runs.AgentRole.Planner"/> in this slice.</summary>
    public AgentRole? AgentRole { get; private set; }

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. Always
    /// <see cref="CollaborationMessage.ProtocolVersionOne"/> in this slice.</summary>
    public string? AgentProtocolVersion { get; private set; }

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. Always
    /// <see cref="CollaborationMessageType.Proposal"/> in this slice — the single message type an
    /// attempt's own claimed intent expects. Never used to represent the Accepted/Challenged
    /// union a critical-review attempt may really produce; see <see cref="AgentResponseContract"/>
    /// for that.</summary>
    public CollaborationMessageType? AgentExpectedMessageType { get; private set; }

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. The closed
    /// shape of durable collaboration fact this attempt is expected to produce — fixed by exactly
    /// one Domain factory (<see cref="ClaimAgent"/> always sets <see cref="Runs.AgentResponseContract.Proposal"/>;
    /// <see cref="ClaimAgentCriticalReview"/> always sets <see cref="Runs.AgentResponseContract.CriticalReview"/>),
    /// never caller-selected.</summary>
    public AgentResponseContract? AgentResponseContract { get; private set; }

    /// <summary>The exact <see cref="CollaborationMessage"/> this attempt reviews. Required and
    /// immutable for a <see cref="Runs.AgentRole.CriticalReviewer"/> attempt; always
    /// <see langword="null"/> for a <see cref="Runs.AgentRole.Planner"/> attempt, which has no
    /// input message to review.</summary>
    public Guid? AgentInputCollaborationMessageId { get; private set; }

    /// <summary>The Ready workspace this attempt is evidence about. Only set when
    /// <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>.</summary>
    public Guid? AgentGitWorkspaceId { get; private set; }

    /// <summary>The exact checkpoint this attempt committed to at claim time. Only set when
    /// <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>.</summary>
    public Guid? AgentGitCheckpointId { get; private set; }

    /// <summary>The checkpoint's fingerprint at claim time — compared against a fresh capture
    /// both immediately before dispatch and immediately after the provider exits. Only set when
    /// <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>.</summary>
    public string? AgentCheckpointFingerprintSha256 { get; private set; }

    /// <summary>Identity of the bounded, versioned context-manifest <see cref="Artifact"/> this
    /// attempt was launched with. The manifest's content lives only in the artifact store; this
    /// is the durable link to it. Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>.</summary>
    public Guid? AgentContextManifestArtifactId { get; private set; }

    public TimeSpan? AgentTimeout { get; private set; }

    public int? AgentMaxBytesPerStream { get; private set; }

    public int? AgentMaxTotalCapturedBytes { get; private set; }

    /// <summary>When the provider was durably committed to being invoked — set once, before the
    /// adapter is ever invoked, and never cleared. Distinguishes "claimed but not yet dispatched"
    /// (<see langword="null"/>) from "dispatched" (the provider has been invoked at most once for
    /// this attempt and must never be invoked again). Left <see langword="null"/> forever when
    /// source drift is detected before dispatch — that attempt still completes truthfully via
    /// <see cref="CompleteAgent"/> without ever having been dispatched.</summary>
    public DateTimeOffset? AgentDispatchedAtUtc { get; private set; }

    /// <summary>How this Agent attempt's terminal state was reached. Only set once <see cref="Kind"/>
    /// is <see cref="AttemptKind.Agent"/> and the attempt reaches <see cref="AttemptStatus.Completed"/>
    /// or <see cref="AttemptStatus.Failed"/>.</summary>
    public AgentOutcome? AgentOutcome { get; private set; }

    /// <summary>A provider-reported session identifier, recorded only when the provider's own
    /// output actually reported one — never invented, never required, never used by this slice
    /// to authorize or correlate anything.</summary>
    public string? AgentProviderSessionId { get; private set; }

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
    /// The execution-start claim for an Agent attempt: durably committed in its own short
    /// transaction before the provider is ever invoked. Once set, this attempt is never eligible
    /// for dispatch again — the guard against invoking the provider more than once, independent
    /// of whether a terminal result is ever later recorded.
    /// </summary>
    public void MarkAgentDispatched(DateTimeOffset nowUtc)
    {
        if (Kind != AttemptKind.Agent)
        {
            throw new InvalidOperationException("Only an Agent attempt can be marked dispatched.");
        }

        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot mark an attempt dispatched that is {Status}.");
        }

        if (AgentDispatchedAtUtc.HasValue)
        {
            throw new InvalidOperationException("This attempt was already marked dispatched.");
        }

        AgentDispatchedAtUtc = nowUtc;
    }

    /// <summary>
    /// Records a provider-reported session identifier. Only callable before the attempt reaches
    /// a terminal state, and only ever once — a later report never silently overwrites an earlier
    /// one, since that would suggest two different provider sessions were conflated into one
    /// attempt.
    /// </summary>
    public void RecordAgentProviderSessionId(string providerSessionId)
    {
        if (Kind != AttemptKind.Agent)
        {
            throw new InvalidOperationException("Only an Agent attempt can record a provider session identifier.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot record a provider session identifier for an attempt that is {Status}.");
        }

        if (AgentProviderSessionId is not null)
        {
            throw new InvalidOperationException("A provider session identifier was already recorded for this attempt.");
        }

        AgentProviderSessionId = providerSessionId;
    }

    /// <summary>
    /// The single atomic completion transition for an Agent attempt: records the outcome and
    /// derives the resulting <see cref="AttemptStatus"/> in one step. Only a successful outcome
    /// for this attempt's own <see cref="AgentResponseContract"/> — <see cref="Runs.AgentOutcome.Proposed"/>
    /// for <see cref="Runs.AgentResponseContract.Proposal"/>; <see cref="Runs.AgentOutcome.Accepted"/>
    /// or <see cref="Runs.AgentOutcome.Challenged"/> for <see cref="Runs.AgentResponseContract.CriticalReview"/> —
    /// completes successfully; every other outcome, and any success outcome that does not match
    /// this attempt's own contract, is a truthful failure. Callable whether or not
    /// <see cref="AgentDispatchedAtUtc"/> was ever set — a pre-dispatch source-change detection
    /// completes an Agent attempt that was never actually dispatched to the provider, which is
    /// itself a truthful outcome, never an invented one.
    /// </summary>
    /// <param name="completionFingerprintSha256">When provided, this is the freshest Git evidence
    /// fingerprint observed for this attempt's workspace. If it does not match the fingerprint
    /// this attempt committed to at claim time, the recorded outcome is unconditionally
    /// <see cref="Runs.AgentOutcome.SourceChanged"/> regardless of <paramref name="outcome"/> —
    /// the same rule <c>VerificationExecution.Complete</c> applies, so a caller can never record a
    /// successful outcome (or any other outcome) as evidence about a checkpoint that no longer
    /// reflects the workspace's real content. Null only when the caller already knows the outcome
    /// is source drift detected before the provider was ever invoked (no fresh completion
    /// evidence to compare — the pre-dispatch fingerprint mismatch itself was already the entire
    /// evidence).</param>
    public void CompleteAgent(AgentOutcome outcome, string? completionFingerprintSha256, DateTimeOffset nowUtc)
    {
        if (Kind != AttemptKind.Agent)
        {
            throw new InvalidOperationException("Only an Agent attempt can record an agent outcome.");
        }

        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot complete an attempt that is {Status}.");
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a defined agent outcome.");
        }

        var effectiveOutcome = completionFingerprintSha256 is not null
            && !string.Equals(completionFingerprintSha256, AgentCheckpointFingerprintSha256, StringComparison.Ordinal)
                ? Runs.AgentOutcome.SourceChanged
                : outcome;

        var isSuccess = effectiveOutcome is Runs.AgentOutcome.Proposed or Runs.AgentOutcome.Accepted or Runs.AgentOutcome.Challenged;

        // Independent Domain-level backstop, never the only line of defense (the Application
        // boundary that records a provider result rejects both cases before ever reaching this
        // call) — a successful outcome is never observable for an attempt that was never actually
        // dispatched to the provider, or for one recorded without fresh evidence confirming the
        // checkpoint it claims to be about, or for one that does not match this attempt's own
        // response contract (a Proposal can never be recorded for a critical-review attempt, and
        // an Accepted/Challenged can never be recorded for a planning attempt).
        if (isSuccess)
        {
            if (!AgentDispatchedAtUtc.HasValue)
            {
                throw new InvalidOperationException($"{effectiveOutcome} cannot be recorded for an attempt that was never dispatched.");
            }

            if (string.IsNullOrWhiteSpace(completionFingerprintSha256))
            {
                throw new InvalidOperationException($"{effectiveOutcome} cannot be recorded without fresh completion evidence.");
            }

            var contractAllows = effectiveOutcome == Runs.AgentOutcome.Proposed
                ? AgentResponseContract == Runs.AgentResponseContract.Proposal
                : AgentResponseContract == Runs.AgentResponseContract.CriticalReview;
            if (!contractAllows)
            {
                throw new InvalidOperationException($"{effectiveOutcome} is not a valid outcome for this attempt's response contract.");
            }
        }

        AgentOutcome = effectiveOutcome;
        Status = isSuccess ? AttemptStatus.Completed : AttemptStatus.Failed;
        CompletedAtUtc = nowUtc;
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
