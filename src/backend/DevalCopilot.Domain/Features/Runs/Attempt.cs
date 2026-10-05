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
        DateTimeOffset claimedAtUtc,
        int agentBudgetSlot)
        => ClaimAgentWithAssignment(
            id, runId, attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256,
            contextManifestArtifactId, timeout, maxBytesPerStream, maxTotalCapturedBytes, claimedAtUtc,
            requestedModel: null, requestedEffort: null, agentBudgetSlot);

    /// <summary>Claims a Codex planning Agent attempt with an owner-requested model/effort pair
    /// already validated by the caller against a fresh catalog observation — never validated here,
    /// since Domain performs no I/O. Bounded exactly like every other assignment identifier on
    /// this entity. <see cref="ClaimAgent"/> is the fixed-null convenience overload every existing
    /// caller keeps using unchanged.</summary>
    public static Attempt ClaimAgentWithAssignment(
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
        DateTimeOffset claimedAtUtc,
        string? requestedModel,
        string? requestedEffort,
        int agentBudgetSlot)
        => ClaimPlanningAttempt(
            id, runId, attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256,
            contextManifestArtifactId, timeout, maxBytesPerStream, maxTotalCapturedBytes, claimedAtUtc,
            requestedModel, requestedEffort, agentBudgetSlot, repairSourceAttemptId: null);

    /// <summary>
    /// Claims the one human-requested format-repair Codex Planner attempt for a source Planner
    /// attempt that ended <see cref="Runs.AgentOutcome.InvalidStructuredOutput"/>. It is an ordinary
    /// read-only Planner attempt in every respect (same provider, role, contract, permission
    /// profile, and adapter version) plus one immutable link to its source. The link is durable
    /// provenance for lineage and the at-most-one-repair-per-source database backstop — never
    /// evidence that the new attempt corrects or preserves the source response's meaning. Whether
    /// the source is actually eligible is the calling Application handler's responsibility (it
    /// must load the source), so this factory only rejects a structurally impossible link.
    /// </summary>
    public static Attempt ClaimAgentPlanningRepair(
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
        DateTimeOffset claimedAtUtc,
        string? requestedModel,
        string? requestedEffort,
        int agentBudgetSlot,
        Guid repairSourceAttemptId)
    {
        if (repairSourceAttemptId == Guid.Empty || repairSourceAttemptId == id)
        {
            throw new ArgumentException("A repair requires a distinct source attempt identity.", nameof(repairSourceAttemptId));
        }

        return ClaimPlanningAttempt(
            id, runId, attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256,
            contextManifestArtifactId, timeout, maxBytesPerStream, maxTotalCapturedBytes, claimedAtUtc,
            requestedModel, requestedEffort, agentBudgetSlot, repairSourceAttemptId);
    }

    /// <summary>
    /// Whether this attempt may be the source of the one manual format repair: a
    /// <see cref="AttemptStatus.Failed"/> (the only status <c>CompleteAgent</c> records for this outcome),
    /// dispatched Codex Planner/Proposal Agent attempt whose recorded outcome is exactly
    /// <see cref="Runs.AgentOutcome.InvalidStructuredOutput"/> and which is not itself a repair
    /// (no repair chain). Position (latest Agent attempt of the run) and "not already repaired"
    /// are cross-row facts the Application handler checks. Evaluates persisted values only, so an
    /// incoherent row is never eligible.
    /// </summary>
    public bool IsEligiblePlanningRepairSource =>
        Kind == AttemptKind.Agent
        && Status == AttemptStatus.Failed
        && AgentProvider == Runs.AgentProvider.Codex
        && AgentRole == Runs.AgentRole.Planner
        && AgentResponseContract == Runs.AgentResponseContract.Proposal
        && AgentExpectedMessageType == CollaborationMessageType.Proposal
        && AgentOutcome == Runs.AgentOutcome.InvalidStructuredOutput
        && AgentDispatchedAtUtc.HasValue
        && AgentRepairSourceAttemptId is null;

    private static Attempt ClaimPlanningAttempt(
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
        DateTimeOffset claimedAtUtc,
        string? requestedModel,
        string? requestedEffort,
        int agentBudgetSlot,
        Guid? repairSourceAttemptId)
    {
        ValidateAgentClaimArguments(
            attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256, contextManifestArtifactId,
            timeout, maxBytesPerStream, maxTotalCapturedBytes, agentBudgetSlot);
        ValidateAssignmentIdentifier(requestedModel, nameof(requestedModel));
        ValidateAssignmentIdentifier(requestedEffort, nameof(requestedEffort));
        ValidateRequestedAssignmentPair(requestedModel, requestedEffort);

        var contract = AgentAttemptContract.For(Runs.AgentResponseContract.Proposal);

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Agent,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            AgentProvider = Runs.AgentProvider.Codex,
            AgentRole = contract.Role,
            AgentProtocolVersion = CollaborationMessage.ProtocolVersionOne,
            AgentExpectedMessageType = CollaborationMessageType.Proposal,
            AgentResponseContract = contract.ResponseContract,
            AgentGitWorkspaceId = gitWorkspaceId,
            AgentGitCheckpointId = gitCheckpointId,
            AgentCheckpointFingerprintSha256 = checkpointFingerprintSha256,
            AgentContextManifestArtifactId = contextManifestArtifactId,
            AgentTimeout = timeout,
            AgentMaxBytesPerStream = maxBytesPerStream,
            AgentMaxTotalCapturedBytes = maxTotalCapturedBytes,
            AgentRequestedModel = requestedModel,
            AgentRequestedEffort = requestedEffort,
            // Mirrors ClaimAgentImplementation's own fixed-assignment reasoning: the current
            // Codex Planner adapter is read-only (CodexProcessInvoker's fixed "--sandbox
            // read-only") — a concrete, non-Unknown permission profile and a dedicated adapter
            // contract version, never caller-supplied.
            AgentPermissionProfile = Runs.AgentPermissionProfile.ReadOnly,
            AgentAdapterContractVersion = "codex-planning-v1",
            AgentBudgetSlot = agentBudgetSlot,
            AgentRepairSourceAttemptId = repairSourceAttemptId,
        };
    }

    /// <summary>
    /// Claims a Claude Code critical-review Agent attempt — a durable, real invocation reviewing
    /// one exact, already-recorded Codex Proposal. This slice accepts only ClaudeCode, the
    /// CriticalReviewer role, protocol 1.0, and the <see cref="Runs.AgentResponseContract.CriticalReview"/>
    /// contract: those four facts are fixed by this factory, not caller-supplied, so no caller can
    /// construct any other combination — a dedicated factory per attempt shape, never a shared,
    /// loosely validated bag of nullable arguments with <see cref="ClaimAgent"/>. Workspace/checkpoint
    /// identity are passed as bare identifiers — cross-checking that the checkpoint belongs to the
    /// workspace, and that both belong to the intended project, is the calling Application
    /// handler's responsibility (it already loads both entities), keeping this Domain feature
    /// independent of the Projects feature. The exact input message this attempt reviews is never
    /// a field on this entity — it is recorded separately, immediately after this call, as the
    /// one authoritative <see cref="AttemptInputMessage"/> row for this attempt (sequence 0);
    /// keeping input identity in exactly one place, never duplicated onto a second, competing
    /// column here.
    /// </summary>
    public static Attempt ClaimAgentCriticalReview(
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
        DateTimeOffset claimedAtUtc,
        int agentBudgetSlot)
        => ClaimAgentCriticalReviewWithModelRequest(
            id, runId, attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256,
            contextManifestArtifactId, timeout, maxBytesPerStream, maxTotalCapturedBytes, claimedAtUtc,
            requestedClaudeModel: null, requestedClaudeEffort: null, agentBudgetSlot);

    /// <summary>Claims a Claude critical-review attempt with an optional owner-requested Claude
    /// model alias and effort level, snapshotted immutably. <see cref="ClaimAgentCriticalReview"/> is the fixed-null
    /// convenience overload; the pair must satisfy <see cref="ClaudeModelRequest.IsValid"/>.</summary>
    public static Attempt ClaimAgentCriticalReviewWithModelRequest(
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
        DateTimeOffset claimedAtUtc,
        string? requestedClaudeModel,
        string? requestedClaudeEffort,
        int agentBudgetSlot,
        Guid? repairSourceAttemptId = null)
    {
        ValidateAgentClaimArguments(
            attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256, contextManifestArtifactId,
            timeout, maxBytesPerStream, maxTotalCapturedBytes, agentBudgetSlot);
        ValidateClaudeModelRequest(requestedClaudeModel, requestedClaudeEffort);
        ValidateRepairSource(id, repairSourceAttemptId);

        var contract = AgentAttemptContract.For(Runs.AgentResponseContract.CriticalReview);

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Agent,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            AgentProvider = Runs.AgentProvider.ClaudeCode,
            AgentRole = contract.Role,
            AgentProtocolVersion = CollaborationMessage.ProtocolVersionOne,
            // A critical-review attempt's own claimed intent is still, in protocol terms, "expect
            // to produce one Proposal-replying message" — the actual Acceptance/Challenge union
            // this really returns is represented by AgentResponseContract below, never by this
            // field, and never by encoding that union as null.
            AgentExpectedMessageType = CollaborationMessageType.Proposal,
            AgentResponseContract = contract.ResponseContract,
            AgentGitWorkspaceId = gitWorkspaceId,
            AgentGitCheckpointId = gitCheckpointId,
            AgentCheckpointFingerprintSha256 = checkpointFingerprintSha256,
            AgentContextManifestArtifactId = contextManifestArtifactId,
            AgentTimeout = timeout,
            AgentMaxBytesPerStream = maxBytesPerStream,
            AgentMaxTotalCapturedBytes = maxTotalCapturedBytes,
            AgentRequestedModel = requestedClaudeModel,
            AgentRequestedEffort = requestedClaudeEffort,
            // Mirrors ClaimAgentImplementation's own fixed-assignment reasoning: the current
            // ClaudeCriticalReviewAdapter is read-only (its fixed empty "--tools" allowlist and
            // "--permission-mode plan" arguments) — a concrete, non-Unknown permission profile
            // and a dedicated adapter contract version, never caller-supplied.
            AgentPermissionProfile = Runs.AgentPermissionProfile.ReadOnly,
            AgentAdapterContractVersion = ReadOnlyFormatRepairPolicy.CriticalReviewAdapterContractVersion,
            AgentBudgetSlot = agentBudgetSlot,
            AgentRepairSourceAttemptId = repairSourceAttemptId,
        };
    }

    /// <summary>
    /// Claims a Codex challenge-resolution Agent attempt — a durable, real invocation resolving
    /// one exact, already-recorded set of Claude Challenges against the original Proposal they
    /// disputed. This slice accepts only Codex, the Resolver role, protocol 1.0, and the
    /// <see cref="Runs.AgentResponseContract.ChallengeResolution"/> contract: those four facts are
    /// fixed by this factory, not caller-supplied. Exactly like <see cref="ClaimAgentCriticalReview"/>,
    /// the exact ordered input set (the original Proposal, then every Challenge in timeline order)
    /// is never a field on this entity — it is recorded separately, immediately after this call,
    /// as this attempt's own ordered <see cref="AttemptInputMessage"/> rows.
    /// </summary>
    public static Attempt ClaimAgentChallengeResolution(
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
        DateTimeOffset claimedAtUtc,
        int agentBudgetSlot)
        => ClaimAgentChallengeResolutionWithAssignment(
            id, runId, attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256,
            contextManifestArtifactId, timeout, maxBytesPerStream, maxTotalCapturedBytes, claimedAtUtc,
            requestedModel: null, requestedEffort: null, agentBudgetSlot);

    /// <summary>Claims a Codex challenge-resolution Agent attempt with an owner-requested
    /// model/effort pair already validated by the caller against a fresh catalog observation —
    /// never validated here, since Domain performs no I/O. <see cref="ClaimAgentChallengeResolution"/>
    /// is the fixed-null convenience overload every existing caller keeps using unchanged.</summary>
    public static Attempt ClaimAgentChallengeResolutionWithAssignment(
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
        DateTimeOffset claimedAtUtc,
        string? requestedModel,
        string? requestedEffort,
        int agentBudgetSlot,
        Guid? repairSourceAttemptId = null)
    {
        ValidateAgentClaimArguments(
            attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256, contextManifestArtifactId,
            timeout, maxBytesPerStream, maxTotalCapturedBytes, agentBudgetSlot);
        ValidateAssignmentIdentifier(requestedModel, nameof(requestedModel));
        ValidateAssignmentIdentifier(requestedEffort, nameof(requestedEffort));
        ValidateRequestedAssignmentPair(requestedModel, requestedEffort);
        ValidateRepairSource(id, repairSourceAttemptId);

        var contract = AgentAttemptContract.For(Runs.AgentResponseContract.ChallengeResolution);

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Agent,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            AgentProvider = Runs.AgentProvider.Codex,
            AgentRole = contract.Role,
            AgentProtocolVersion = CollaborationMessage.ProtocolVersionOne,
            // Mirrors ClaimAgentCriticalReview's own reasoning: the real Decision-set-plus-revised-
            // Proposal union this attempt produces is represented by AgentResponseContract below,
            // never by this placeholder.
            AgentExpectedMessageType = CollaborationMessageType.Proposal,
            AgentResponseContract = contract.ResponseContract,
            AgentGitWorkspaceId = gitWorkspaceId,
            AgentGitCheckpointId = gitCheckpointId,
            AgentCheckpointFingerprintSha256 = checkpointFingerprintSha256,
            AgentContextManifestArtifactId = contextManifestArtifactId,
            AgentTimeout = timeout,
            AgentMaxBytesPerStream = maxBytesPerStream,
            AgentMaxTotalCapturedBytes = maxTotalCapturedBytes,
            AgentRequestedModel = requestedModel,
            AgentRequestedEffort = requestedEffort,
            // Mirrors ClaimAgentImplementation's own fixed-assignment reasoning: the current
            // Codex Resolver adapter is read-only (CodexProcessInvoker's fixed "--sandbox
            // read-only") — a concrete, non-Unknown permission profile and a dedicated adapter
            // contract version, never caller-supplied.
            AgentPermissionProfile = Runs.AgentPermissionProfile.ReadOnly,
            AgentAdapterContractVersion = ReadOnlyFormatRepairPolicy.ChallengeResolutionAdapterContractVersion,
            AgentBudgetSlot = agentBudgetSlot,
            AgentRepairSourceAttemptId = repairSourceAttemptId,
        };
    }

    /// <summary>
    /// Claims a Codex implementation-review Agent attempt — a durable, real, read-only
    /// invocation reviewing one exact, already-completed Claude implementation result: its
    /// Execution report, its immutable result checkpoint, and the complete set of currently
    /// enabled verification-command executions bound to that same checkpoint. This slice accepts
    /// only Codex, the CodeReviewer role, protocol 1.0, and the
    /// <see cref="Runs.AgentResponseContract.ImplementationReview"/> contract. Exactly like
    /// <see cref="ClaimAgentChallengeResolution"/>, the exact ordered input identity (the
    /// Execution report, then every claimed verification-execution in a deterministic order) is
    /// never a field on this entity — it is recorded separately, immediately after this call, as
    /// this attempt's own ordered <see cref="AttemptInputMessage"/> rows.
    /// </summary>
    public static Attempt ClaimAgentCodeReview(
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
        DateTimeOffset claimedAtUtc,
        int agentBudgetSlot)
        => ClaimAgentCodeReviewWithAssignment(
            id, runId, attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256,
            contextManifestArtifactId, timeout, maxBytesPerStream, maxTotalCapturedBytes, claimedAtUtc,
            requestedModel: null, requestedEffort: null, agentBudgetSlot);

    /// <summary>Claims a Codex implementation-review Agent attempt with an owner-requested
    /// model/effort pair already validated by the caller against a fresh catalog observation —
    /// never validated here, since Domain performs no I/O. <see cref="ClaimAgentCodeReview"/> is
    /// the fixed-null convenience overload every existing caller keeps using unchanged.</summary>
    public static Attempt ClaimAgentCodeReviewWithAssignment(
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
        DateTimeOffset claimedAtUtc,
        string? requestedModel,
        string? requestedEffort,
        int agentBudgetSlot,
        Guid? repairSourceAttemptId = null)
    {
        ValidateAgentClaimArguments(
            attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256, contextManifestArtifactId,
            timeout, maxBytesPerStream, maxTotalCapturedBytes, agentBudgetSlot);
        ValidateAssignmentIdentifier(requestedModel, nameof(requestedModel));
        ValidateAssignmentIdentifier(requestedEffort, nameof(requestedEffort));
        ValidateRequestedAssignmentPair(requestedModel, requestedEffort);
        ValidateRepairSource(id, repairSourceAttemptId);

        var contract = AgentAttemptContract.For(Runs.AgentResponseContract.ImplementationReview);

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Agent,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            AgentProvider = Runs.AgentProvider.Codex,
            AgentRole = contract.Role,
            AgentProtocolVersion = CollaborationMessage.ProtocolVersionOne,
            // Mirrors ClaimAgentChallengeResolution's own reasoning: the real Approval-or-Findings
            // union this attempt produces is represented by AgentResponseContract below, never by
            // this placeholder.
            AgentExpectedMessageType = CollaborationMessageType.ReviewFinding,
            AgentResponseContract = contract.ResponseContract,
            AgentGitWorkspaceId = gitWorkspaceId,
            AgentGitCheckpointId = gitCheckpointId,
            AgentCheckpointFingerprintSha256 = checkpointFingerprintSha256,
            AgentContextManifestArtifactId = contextManifestArtifactId,
            AgentTimeout = timeout,
            AgentMaxBytesPerStream = maxBytesPerStream,
            AgentMaxTotalCapturedBytes = maxTotalCapturedBytes,
            AgentRequestedModel = requestedModel,
            AgentRequestedEffort = requestedEffort,
            // Mirrors ClaimAgentImplementation's own fixed-assignment reasoning: the current
            // Codex CodeReviewer adapter is read-only (CodexProcessInvoker's fixed "--sandbox
            // read-only") — a concrete, non-Unknown permission profile and a dedicated adapter
            // contract version, never caller-supplied.
            AgentPermissionProfile = Runs.AgentPermissionProfile.ReadOnly,
            AgentAdapterContractVersion = ReadOnlyFormatRepairPolicy.ImplementationReviewAdapterContractVersion,
            AgentBudgetSlot = agentBudgetSlot,
            AgentRepairSourceAttemptId = repairSourceAttemptId,
        };
    }

    /// <summary>
    /// Claims a Codex verification-diagnosis Agent attempt (ADR-0018) — a durable, real, read-only invocation diagnosing the
    /// current failed local verification of one exact, already-completed Claude implementation result. It carries the
    /// CodeReviewer role and the <see cref="Runs.AgentResponseContract.VerificationDiagnosis"/> contract, the same
    /// read-only permission profile as an ordinary code review, and its own fixed adapter contract; it is never a format
    /// repair. The exact ordered input identity (the ExecutionReport as the sequence-0 <see cref="AttemptInputMessage"/> and the
    /// ordered claimed executions as <see cref="AttemptVerificationEvidence"/> rows) is recorded separately, immediately after
    /// this call.
    /// </summary>
    public static Attempt ClaimAgentVerificationDiagnosis(
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
        DateTimeOffset claimedAtUtc,
        string? requestedModel,
        string? requestedEffort,
        int agentBudgetSlot)
    {
        ValidateAgentClaimArguments(
            attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256, contextManifestArtifactId,
            timeout, maxBytesPerStream, maxTotalCapturedBytes, agentBudgetSlot);
        ValidateAssignmentIdentifier(requestedModel, nameof(requestedModel));
        ValidateAssignmentIdentifier(requestedEffort, nameof(requestedEffort));
        ValidateRequestedAssignmentPair(requestedModel, requestedEffort);

        var contract = AgentAttemptContract.For(Runs.AgentResponseContract.VerificationDiagnosis);

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Agent,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            AgentProvider = Runs.AgentProvider.Codex,
            AgentRole = contract.Role,
            AgentProtocolVersion = CollaborationMessage.ProtocolVersionOne,
            // The real Findings-or-Escalation union is represented by AgentResponseContract; this placeholder names the
            // primary message type, exactly like the ordinary code review's.
            AgentExpectedMessageType = CollaborationMessageType.ReviewFinding,
            AgentResponseContract = contract.ResponseContract,
            AgentGitWorkspaceId = gitWorkspaceId,
            AgentGitCheckpointId = gitCheckpointId,
            AgentCheckpointFingerprintSha256 = checkpointFingerprintSha256,
            AgentContextManifestArtifactId = contextManifestArtifactId,
            AgentTimeout = timeout,
            AgentMaxBytesPerStream = maxBytesPerStream,
            AgentMaxTotalCapturedBytes = maxTotalCapturedBytes,
            AgentRequestedModel = requestedModel,
            AgentRequestedEffort = requestedEffort,
            AgentPermissionProfile = Runs.AgentPermissionProfile.ReadOnly,
            AgentAdapterContractVersion = VerificationDiagnosisPolicy.AdapterContractVersion,
            AgentBudgetSlot = agentBudgetSlot,
        };
    }

    /// <summary>
    /// Claims the default Claude Code initial implementation Agent attempt — a durable, real invocation
    /// implementing one exact, already-resolved plan (an accepted original Proposal or a
    /// resolved revised Proposal) inside the owned worktree. The assignment overload fixes
    /// ClaudeCode, the Implementer role, protocol 1.0, and the <see cref="Runs.AgentResponseContract.ImplementationReport"/>
    /// contract. <paramref name="gitCheckpointId"/> is this attempt's own immutable STARTING
    /// checkpoint — never overwritten by a later result; a successful implementation's resulting
    /// checkpoint is recorded separately via <see cref="CompleteImplementation"/> as
    /// <see cref="AgentResultGitCheckpointId"/>. Exactly like <see cref="ClaimAgentChallengeResolution"/>,
    /// the exact ordered input identity (the implemented Proposal, then its Acceptance or ordered
    /// Decision set) is never a field on this entity — it is recorded separately, immediately
    /// after this call, as this attempt's own ordered <see cref="AttemptInputMessage"/> rows.
    /// </summary>
    public static Attempt ClaimAgentImplementation(
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
        DateTimeOffset claimedAtUtc,
        int agentBudgetSlot)
        => ClaimAgentImplementationWithAssignment(
            id, runId, attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256,
            contextManifestArtifactId, timeout, maxBytesPerStream, maxTotalCapturedBytes, claimedAtUtc,
            requestedModel: null, requestedEffort: null,
            Runs.AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV1, agentBudgetSlot);

    /// <summary>Claims an initial Claude Code ImplementationReport attempt with a Claude model/effort
    /// request that must satisfy <see cref="ClaudeModelRequest.IsValid"/> and bounded,
    /// immutable requested assignment facts. Provider identity remains fixed by this supported
    /// execution path; it is provenance, not semantic authority.</summary>
    public static Attempt ClaimAgentImplementationWithAssignment(
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
        DateTimeOffset claimedAtUtc,
        string? requestedModel,
        string? requestedEffort,
        AgentPermissionProfile permissionProfile,
        string adapterContractVersion,
        int agentBudgetSlot,
        int? requestedMaxTurns = null,
        string? directHumanGuidance = null)
    {
        ValidateAgentClaimArguments(
            attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256, contextManifestArtifactId,
            timeout, maxBytesPerStream, maxTotalCapturedBytes, agentBudgetSlot);

        var contract = AgentAttemptContract.For(Runs.AgentResponseContract.ImplementationReport);

        if (!Enum.IsDefined(permissionProfile) || permissionProfile == Runs.AgentPermissionProfile.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(permissionProfile), permissionProfile, "A concrete implementation permission profile is required.");
        }

        if (string.IsNullOrWhiteSpace(adapterContractVersion) || adapterContractVersion.Length > 128)
        {
            throw new ArgumentException("An adapter contract version must be non-blank and at most 128 characters.", nameof(adapterContractVersion));
        }
        ValidateAssignmentIdentifier(requestedModel, nameof(requestedModel));
        ValidateAssignmentIdentifier(requestedEffort, nameof(requestedEffort));
        ValidateClaudeModelRequest(requestedModel, requestedEffort);
        ValidateMutationTurnLimit(
            requestedMaxTurns,
            adapterContractVersion == ClaudeMutationAdapterContract.ImplementationV2
                && permissionProfile == Runs.AgentPermissionProfile.WorkspaceEditOnly);
        ValidateDirectHumanGuidance(
            directHumanGuidance,
            adapterContractVersion == ClaudeMutationAdapterContract.ImplementationV2
                && permissionProfile == Runs.AgentPermissionProfile.WorkspaceEditOnly);

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Agent,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            AgentProvider = Runs.AgentProvider.ClaudeCode,
            AgentRole = contract.Role,
            AgentProtocolVersion = CollaborationMessage.ProtocolVersionOne,
            _agentRequestedMaxTurns = ClaudeMutationTurnLimit.Format(requestedMaxTurns),
            _agentDirectHumanGuidance = directHumanGuidance,
            // Mirrors ClaimAgentCriticalReview/ClaimAgentChallengeResolution's own reasoning:
            // the real ExecutionReport this attempt produces is represented by
            // AgentResponseContract below, never by this placeholder.
            AgentExpectedMessageType = CollaborationMessageType.Proposal,
            AgentResponseContract = contract.ResponseContract,
            AgentGitWorkspaceId = gitWorkspaceId,
            AgentGitCheckpointId = gitCheckpointId,
            AgentCheckpointFingerprintSha256 = checkpointFingerprintSha256,
            AgentContextManifestArtifactId = contextManifestArtifactId,
            AgentTimeout = timeout,
            AgentMaxBytesPerStream = maxBytesPerStream,
            AgentMaxTotalCapturedBytes = maxTotalCapturedBytes,
            AgentRequestedModel = requestedModel,
            AgentRequestedEffort = requestedEffort,
            AgentPermissionProfile = permissionProfile,
            AgentAdapterContractVersion = adapterContractVersion,
            AgentBudgetSlot = agentBudgetSlot,
        };
    }

    private static void ValidateAssignmentIdentifier(string? value, string paramName)
    {
        if (!IsValidAssignmentIdentifier(value))
        {
            throw new ArgumentException("Assignment identifiers must be blank or at most 128 characters.", paramName);
        }
    }

    private static void ValidateRepairSource(Guid attemptId, Guid? repairSourceAttemptId)
    {
        if (repairSourceAttemptId is { } sourceId && (sourceId == Guid.Empty || sourceId == attemptId))
        {
            throw new ArgumentException("A repair requires a distinct source attempt identity.", nameof(repairSourceAttemptId));
        }
    }

    private static void ValidateClaudeModelRequest(string? model, string? effort)
    {
        if (!ClaudeModelRequest.IsValid(model, effort))
        {
            throw new ArgumentException(
                "A requested Claude model must be a supported alias, and an effort is valid only with sonnet or opus.",
                nameof(model));
        }
    }

    /// <summary>A requested turn limit must be valid and may only be stored with a contract version that
    /// can carry it, so a limit is never recorded beside an invocation that would omit it.</summary>
    private static void ValidateMutationTurnLimit(int? requestedMaxTurns, bool contractCarriesTurnLimit)
    {
        if (!ClaudeMutationTurnLimit.IsValid(requestedMaxTurns))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedMaxTurns),
                requestedMaxTurns,
                $"A requested Claude turn limit must be between {ClaudeMutationTurnLimit.Minimum} and {ClaudeMutationTurnLimit.Maximum}.");
        }

        if (requestedMaxTurns is not null && !contractCarriesTurnLimit)
        {
            throw new ArgumentException(
                "A requested Claude turn limit requires the version 2 mutation contract with the workspace-edit profile.",
                nameof(requestedMaxTurns));
        }
    }

    /// <summary>Direct guidance must already be exactly its own normalized form, and may only be stored with a contract
    /// that can carry it, so guidance is never recorded beside an invocation whose sealed context would omit it.</summary>
    private static void ValidateDirectHumanGuidance(string? directHumanGuidance, bool contractCarriesGuidance)
    {
        if (directHumanGuidance is null)
        {
            return;
        }

        if (DirectHumanGuidance.Read(directHumanGuidance).IsMalformed)
        {
            throw new ArgumentException(
                "Direct human guidance must be accepted, normalized text.", nameof(directHumanGuidance));
        }

        if (!contractCarriesGuidance)
        {
            throw new ArgumentException(
                "Direct human guidance requires the version 2 mutation contract with the workspace-edit profile.",
                nameof(directHumanGuidance));
        }
    }

    private static bool IsValidAssignmentIdentifier(string? value) =>
        value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= 128);

    /// <summary>Shared cross-field guard for the three current Codex claim factories that accept
    /// an owner-requested model/effort pair: a requested effort is never accepted without a
    /// requested model, mirroring <c>Run.SetRequestedCodexAssignment</c>'s own identical
    /// invariant. <see cref="ClaimAgentImplementationWithAssignment"/> is deliberately unaffected —
    /// it is a distinct, already-reviewed contract outside this slice's boundary.</summary>
    private static void ValidateRequestedAssignmentPair(string? requestedModel, string? requestedEffort)
    {
        if (requestedModel is null && requestedEffort is not null)
        {
            throw new ArgumentException("A requested effort requires a requested model.", nameof(requestedEffort));
        }
    }

    /// <summary>
    /// Claims an Implementer review-correction Agent attempt — a durable, real, workspace-mutating
    /// invocation that corrects the implementation in response to one exact, already-recorded set
    /// of Codex ReviewFinding messages. This slice accepts only ClaudeCode, the Implementer role,
    /// protocol 1.0, and the <see cref="Runs.AgentResponseContract.ReviewCorrection"/> contract:
    /// those four facts are fixed by this factory, not caller-supplied — a dedicated factory per
    /// attempt shape, never a shared, loosely validated bag of nullable arguments. Exactly like
    /// <see cref="ClaimAgentImplementation"/>, <paramref name="gitCheckpointId"/> is this attempt's
    /// own immutable STARTING checkpoint — a successful correction's resulting checkpoint is
    /// recorded separately via <see cref="CompleteReviewCorrection"/> as
    /// <see cref="AgentResultGitCheckpointId"/>. The exact ordered input identity (the previous
    /// ExecutionReport, then every ReviewFinding in timeline order) is never a field on this entity
    /// — it is recorded separately, immediately after this call, as this attempt's own ordered
    /// <see cref="AttemptInputMessage"/> rows.
    /// </summary>
    /// <remarks>This overload is the historical version 1 mutation contract: no turn-limit option can be carried.
    /// It is retained as the fixed-null convenience overload (and to seed version 1 history in tests); every
    /// new claim uses <see cref="ClaimAgentReviewCorrectionWithModelRequest"/>.</remarks>
    public static Attempt ClaimAgentReviewCorrection(
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
        DateTimeOffset claimedAtUtc,
        int agentBudgetSlot)
        => ClaimReviewCorrection(
            id, runId, attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256,
            contextManifestArtifactId, timeout, maxBytesPerStream, maxTotalCapturedBytes, claimedAtUtc,
            requestedClaudeModel: null, requestedClaudeEffort: null, agentBudgetSlot,
            ClaudeMutationAdapterContract.ReviewCorrectionV1, requestedMaxTurns: null);

    /// <summary>Claims a review-correction attempt under the version 2 mutation contract with an optional
    /// owner-requested Claude model alias and effort level and an optional agentic-turn limit, all
    /// snapshotted immutably. The pair must satisfy <see cref="ClaudeModelRequest.IsValid"/> and the
    /// limit <see cref="ClaudeMutationTurnLimit.IsValid"/>.</summary>
    public static Attempt ClaimAgentReviewCorrectionWithModelRequest(
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
        DateTimeOffset claimedAtUtc,
        string? requestedClaudeModel,
        string? requestedClaudeEffort,
        int agentBudgetSlot,
        int? requestedMaxTurns = null,
        string? directHumanGuidance = null)
        => ClaimReviewCorrection(
            id, runId, attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256,
            contextManifestArtifactId, timeout, maxBytesPerStream, maxTotalCapturedBytes, claimedAtUtc,
            requestedClaudeModel, requestedClaudeEffort, agentBudgetSlot,
            ClaudeMutationAdapterContract.ReviewCorrectionV2, requestedMaxTurns, directHumanGuidance);

    private static Attempt ClaimReviewCorrection(
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
        DateTimeOffset claimedAtUtc,
        string? requestedClaudeModel,
        string? requestedClaudeEffort,
        int agentBudgetSlot,
        string adapterContractVersion,
        int? requestedMaxTurns,
        string? directHumanGuidance = null)
    {
        ValidateAgentClaimArguments(
            attemptNumber, gitWorkspaceId, gitCheckpointId, checkpointFingerprintSha256, contextManifestArtifactId,
            timeout, maxBytesPerStream, maxTotalCapturedBytes, agentBudgetSlot);
        ValidateClaudeModelRequest(requestedClaudeModel, requestedClaudeEffort);
        ValidateMutationTurnLimit(
            requestedMaxTurns, adapterContractVersion == ClaudeMutationAdapterContract.ReviewCorrectionV2);
        ValidateDirectHumanGuidance(
            directHumanGuidance, adapterContractVersion == ClaudeMutationAdapterContract.ReviewCorrectionV2);

        var contract = AgentAttemptContract.For(Runs.AgentResponseContract.ReviewCorrection);

        return new Attempt
        {
            Id = id,
            RunId = runId,
            AttemptNumber = attemptNumber,
            Kind = AttemptKind.Agent,
            Status = AttemptStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
            AgentProvider = Runs.AgentProvider.ClaudeCode,
            AgentRole = contract.Role,
            AgentProtocolVersion = CollaborationMessage.ProtocolVersionOne,
            // The real output of a ReviewCorrection attempt is one RevisionResponse per ReviewFinding
            // plus one ExecutionReport — both are represented by AgentResponseContract below, never
            // by this placeholder, which signals the primary conversational message type expected.
            AgentExpectedMessageType = CollaborationMessageType.RevisionResponse,
            AgentResponseContract = contract.ResponseContract,
            AgentGitWorkspaceId = gitWorkspaceId,
            AgentGitCheckpointId = gitCheckpointId,
            AgentCheckpointFingerprintSha256 = checkpointFingerprintSha256,
            AgentContextManifestArtifactId = contextManifestArtifactId,
            AgentTimeout = timeout,
            AgentMaxBytesPerStream = maxBytesPerStream,
            AgentMaxTotalCapturedBytes = maxTotalCapturedBytes,
            AgentRequestedModel = requestedClaudeModel,
            AgentRequestedEffort = requestedClaudeEffort,
            // Mirrors ClaimAgentImplementation's own fixed-assignment reasoning: the current
            // ClaudeReviewCorrectionAdapter passes the same workspace-edit allowlist and
            // permission mode as the initial implementation adapter — a concrete, non-Unknown
            // permission profile and a dedicated adapter contract version, never caller-supplied.
            AgentPermissionProfile = Runs.AgentPermissionProfile.WorkspaceEditOnly,
            AgentAdapterContractVersion = adapterContractVersion,
            _agentRequestedMaxTurns = ClaudeMutationTurnLimit.Format(requestedMaxTurns),
            _agentDirectHumanGuidance = directHumanGuidance,
            AgentBudgetSlot = agentBudgetSlot,
        };
    }

    /// <summary>Shared argument validation for every Agent-attempt claim factory beyond the
    /// original <see cref="ClaimAgent"/> — kept as one private helper rather than copy-pasted so
    /// the bounds can never silently drift apart between shapes.</summary>
    private static void ValidateAgentClaimArguments(
        int attemptNumber,
        Guid gitWorkspaceId,
        Guid gitCheckpointId,
        string checkpointFingerprintSha256,
        Guid contextManifestArtifactId,
        TimeSpan timeout,
        int maxBytesPerStream,
        int maxTotalCapturedBytes,
        int agentBudgetSlot)
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

        if (agentBudgetSlot < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(agentBudgetSlot), agentBudgetSlot, "A claimed Agent attempt requires a positive budget slot.");
        }
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

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. Provider
    /// identity is durable assignment/provenance, never semantic authority.</summary>
    public AgentProvider? AgentProvider { get; private set; }

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. The role
    /// remains the semantic authority dimension.</summary>
    public AgentRole? AgentRole { get; private set; }

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. Always
    /// <see cref="CollaborationMessage.ProtocolVersionOne"/> in this slice.</summary>
    public string? AgentProtocolVersion { get; private set; }

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. The
    /// factory-selected primary message type an attempt's own claimed intent expects. It is never
    /// used to represent the Accepted/Challenged critical-review union or the
    /// RevisionResponse-plus-ExecutionReport correction contract; see
    /// <see cref="AgentResponseContract"/> for those closed shapes.</summary>
    public CollaborationMessageType? AgentExpectedMessageType { get; private set; }

    /// <summary>Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>. The closed
    /// shape of durable collaboration fact this attempt is expected to produce — fixed by exactly
    /// one Domain factory (<see cref="ClaimAgent"/> sets <see cref="Runs.AgentResponseContract.Proposal"/>;
    /// <see cref="ClaimAgentCriticalReview"/> sets <see cref="Runs.AgentResponseContract.CriticalReview"/>;
    /// <see cref="ClaimAgentChallengeResolution"/> sets <see cref="Runs.AgentResponseContract.ChallengeResolution"/>;
    /// <see cref="ClaimAgentReviewCorrection"/> sets <see cref="Runs.AgentResponseContract.ReviewCorrection"/>),
    /// never caller-selected.</summary>
    public AgentResponseContract? AgentResponseContract { get; private set; }

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

    /// <summary>The permanent, run-wide Agent claim-budget slot this attempt consumed at claim
    /// time — 1-based, unique per <see cref="RunId"/> among every Agent attempt this run has ever
    /// claimed, and never reassigned or freed even if the attempt later fails or is interrupted.
    /// Only set when <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>; a Simulated or Process
    /// attempt never consumes this budget.</summary>
    public int? AgentBudgetSlot { get; private set; }

    /// <summary>The attempt this attempt is the one manual format repair of — immutable lineage set only by
    /// <see cref="ClaimAgentPlanningRepair"/> or the repair-source argument of the CriticalReviewer, Resolver, and
    /// CodeReviewer claim factories (see <see cref="ReadOnlyFormatRepairPolicy"/>), otherwise <see langword="null"/>.
    /// A filtered unique index allows at most one repair per source, and an attempt with a source is
    /// never itself an eligible source, so repairs never chain.</summary>
    public Guid? AgentRepairSourceAttemptId { get; private set; }

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

    /// <summary>The ADR-0009 execution-effect classification of this attempt's own response
    /// contract — a pure, non-persisted function of <see cref="AgentResponseContract"/> via
    /// <see cref="AgentAttemptContract"/>, never separately stored or caller-set.
    /// <see langword="null"/> unless <see cref="Kind"/> is <see cref="AttemptKind.Agent"/>.
    /// Fails closed (throws) rather than returning a default if an Agent attempt somehow holds
    /// no response contract or an undefined one — that shape is never produced by any factory
    /// on this type today, but this property never silently treats it as
    /// <see cref="AgentEffectKind.ReadOnly"/>.</summary>
    public AgentEffectKind? AgentEffect
    {
        get
        {
            if (Kind != AttemptKind.Agent)
            {
                return null;
            }

            if (AgentResponseContract is not { } responseContract)
            {
                throw new InvalidOperationException("An Agent attempt must have a response contract.");
            }

            return AgentAttemptContract.For(responseContract).Effect;
        }
    }

    /// <summary>A provider-reported session identifier, recorded only when the provider's own
    /// output actually reported one — never invented, never required, never used by this slice
    /// to authorize or correlate anything.</summary>
    public string? AgentProviderSessionId { get; private set; }

    public string? AgentRequestedModel { get; private set; }

    public string? AgentObservedModel { get; private set; }

    public string? AgentRequestedEffort { get; private set; }

    public string? AgentObservedEffort { get; private set; }

    public AgentPermissionProfile? AgentPermissionProfile { get; private set; }

    public string? AgentAdapterContractVersion { get; private set; }
    /// <summary>The EF field-only property that holds the snapshot as its exact stored text. Persistence and
    /// queries refer to this name; every other reader uses <see cref="ReadAgentRequestedMaxTurns"/>.</summary>
    public const string AgentRequestedMaxTurnsStorageProperty = "_agentRequestedMaxTurns";

    private string? _agentRequestedMaxTurns;

    /// <summary>The exact reading of the immutable Claude agentic-turn-limit request this mutation attempt was
    /// claimed with (passed as <c>--max-turns</c>). Absent means none was recorded; malformed means the stored
    /// representation is not a canonical whole number in range, so it is never read as a request, as null, or as
    /// zero. Only a version 2 mutation contract can carry one (see <see cref="ClaudeMutationAdapterContract"/>).
    /// A claim-time snapshot of the owner's request, never a measured or observed turn count.</summary>
    public ClaudeMutationTurnLimitReading ReadAgentRequestedMaxTurns() => ClaudeMutationTurnLimit.Read(_agentRequestedMaxTurns);

    /// <summary>The valid snapshot, or <see langword="null"/> when none was recorded. Throws when the stored value
    /// is malformed, so a malformed snapshot can never be mistaken for "none".</summary>
    public int? AgentRequestedMaxTurns => ReadAgentRequestedMaxTurns() is { IsMalformed: false } reading
        ? reading.Value
        : throw new InvalidOperationException("The stored Claude turn-limit snapshot is malformed.");

    /// <summary>What this attempt's own stored facts say about a turn-limit request, by exact
    /// version-aware mapping (see <see cref="ClaudeMutationAdapterContract.Classify"/>).</summary>
    public ClaudeMutationTurnLimitEvidence GetMutationTurnLimitEvidence() => ClaudeMutationAdapterContract.Classify(
        AgentResponseContract, AgentRole, AgentProvider, AgentPermissionProfile, AgentAdapterContractVersion, ReadAgentRequestedMaxTurns());

    /// <summary>Whether a provider may be invoked for this attempt as far as its turn-limit snapshot is concerned:
    /// an attempt that recorded none is unaffected, and one that recorded a request needs a well-formed request
    /// and the complete coherent role, provider, response-contract, permission-profile, and exact version 2
    /// tuple (see <see cref="ClaudeMutationAdapterContract.IsDispatchCoherent"/>).</summary>
    public bool HasDispatchCoherentTurnLimit() => ClaudeMutationAdapterContract.IsDispatchCoherent(
        AgentResponseContract, AgentRole, AgentProvider, AgentPermissionProfile, AgentAdapterContractVersion, ReadAgentRequestedMaxTurns());

    /// <summary>The EF field-only property that holds the account-usage stop snapshot as its exact stored text. Persistence and
    /// queries refer to this name; every other reader uses <see cref="ReadAgentCodexAccountUsageStopPercent"/>.</summary>
    public const string AgentCodexAccountUsageStopStorageProperty = "_agentCodexAccountUsageStopPercent";

    private string? _agentCodexAccountUsageStopPercent;

    /// <summary>The exact reading of the immutable Codex account-usage stop (a used-percent threshold, see
    /// <see cref="CodexAccountUsageStop"/>) this Codex attempt was claimed with. Absent means the stop was not configured when it
    /// was claimed, or the attempt predates the setting (no historical policy is ever invented); malformed means the stored
    /// representation is not a canonical whole number in range, so it is never read as a threshold, as null, or as zero. A
    /// claim-time snapshot of the owner's setting, never a statement about the account.</summary>
    public CodexAccountUsageStopReading ReadAgentCodexAccountUsageStopPercent() =>
        CodexAccountUsageStop.Read(_agentCodexAccountUsageStopPercent);

    /// <summary>Records the run's account-usage stop on this attempt, exactly once, before the attempt is first persisted. Only a
    /// Running, never-dispatched Codex Agent attempt can take it, and the value must satisfy
    /// <see cref="CodexAccountUsageStop.IsValid"/>. There is no way to change or clear it afterwards.</summary>
    public void SnapshotCodexAccountUsageStop(int percent)
    {
        if (Kind != AttemptKind.Agent || AgentProvider != Runs.AgentProvider.Codex)
        {
            throw new InvalidOperationException("Only a Codex Agent attempt can snapshot an account-usage stop.");
        }

        if (Status != AttemptStatus.Running || AgentDispatchedAtUtc.HasValue)
        {
            throw new InvalidOperationException("The account-usage stop is snapshotted when the attempt is claimed, before dispatch.");
        }

        if (_agentCodexAccountUsageStopPercent is not null)
        {
            throw new InvalidOperationException("The account-usage stop snapshot is immutable.");
        }

        _agentCodexAccountUsageStopPercent = CodexAccountUsageStop.Format(percent);
    }

    /// <summary>The EF field-only property that holds the direct-guidance snapshot as its exact stored text.
    /// Persistence and queries refer to this name; every other reader uses <see cref="ReadAgentDirectHumanGuidance"/>.</summary>
    public const string AgentDirectHumanGuidanceStorageProperty = "_agentDirectHumanGuidance";

    private string? _agentDirectHumanGuidance;

    /// <summary>The exact reading of the immutable direct human guidance this mutation attempt was claimed with. Assigned
    /// only by the two mutation claims, in the same commit as the attempt; null means none was recorded (it is not
    /// proof that no historical human guidance existed). Malformed means the stored text is not exactly an accepted,
    /// normalized value, so it is never read as guidance, as absence, or as text.</summary>
    public DirectHumanGuidanceReading ReadAgentDirectHumanGuidance() => DirectHumanGuidance.Read(_agentDirectHumanGuidance);

    /// <summary>What this attempt's own stored facts say about direct guidance, by exact version-aware mapping (see
    /// <see cref="ClaudeMutationAdapterContract.ClassifyDirectGuidance"/>).</summary>
    public DirectHumanGuidanceEvidence GetDirectHumanGuidanceEvidence() => ClaudeMutationAdapterContract.ClassifyDirectGuidance(
        AgentResponseContract, AgentRole, AgentProvider, AgentPermissionProfile, AgentAdapterContractVersion, ReadAgentDirectHumanGuidance());

    /// <summary>Whether a provider may be invoked for this attempt as far as its direct-guidance snapshot is concerned:
    /// an attempt that recorded none is unaffected, and one that recorded guidance needs well-formed text and the complete
    /// coherent role, provider, response-contract, permission-profile, and exact version 2 tuple.</summary>
    public bool HasDispatchCoherentDirectHumanGuidance() => ClaudeMutationAdapterContract.IsDirectGuidanceDispatchCoherent(
        AgentResponseContract, AgentRole, AgentProvider, AgentPermissionProfile, AgentAdapterContractVersion, ReadAgentDirectHumanGuidance());

    /// <summary>Returns the assignment facts without introducing a second persisted aggregate.
    /// Non-Agent attempts have no assignment snapshot.</summary>
    public AgentAssignmentSnapshot? GetAssignmentSnapshot()
    {
        if (Kind != AttemptKind.Agent
            || AgentProvider is not { } provider
            || !Enum.IsDefined(provider)
            || !IsValidAssignmentIdentifier(AgentRequestedModel)
            || !IsValidAssignmentIdentifier(AgentObservedModel)
            || !IsValidAssignmentIdentifier(AgentRequestedEffort)
            || !IsValidAssignmentIdentifier(AgentObservedEffort)
            || ReadAgentRequestedMaxTurns().IsMalformed
            || (AgentAdapterContractVersion is { } contractVersion
                && (string.IsNullOrWhiteSpace(contractVersion) || contractVersion.Length > 128)))
        {
            return null;
        }

        var permissionProfile = AgentPermissionProfile ?? Runs.AgentPermissionProfile.Unknown;
        if (!Enum.IsDefined(permissionProfile))
        {
            return null;
        }

        return new AgentAssignmentSnapshot(
            provider,
            AgentRequestedModel,
            AgentObservedModel,
            AgentRequestedEffort,
            AgentObservedEffort,
            permissionProfile,
            AgentAdapterContractVersion,
            ReadAgentRequestedMaxTurns().Value);
    }

    /// <summary>Records provider-reported assignment facts exactly once. A provider that does
    /// not authoritatively report model or effort leaves both values null and records nothing.</summary>
    public void RecordAgentObservedAssignment(string? observedModel, string? observedEffort)
    {
        if (Kind != AttemptKind.Agent)
        {
            throw new InvalidOperationException("Only an Agent attempt can record assignment observations.");
        }

        ValidateAssignmentIdentifier(observedModel, nameof(observedModel));
        ValidateAssignmentIdentifier(observedEffort, nameof(observedEffort));
        if (observedModel is null && observedEffort is null)
        {
            return;
        }

        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot record assignment observations for an attempt that is {Status}.");
        }

        if (AgentObservedModel is not null || AgentObservedEffort is not null)
        {
            throw new InvalidOperationException("Provider assignment observations are write-once.");
        }

        AgentObservedModel = observedModel;
        AgentObservedEffort = observedEffort;
    }

    /// <summary>The new, immutable <c>GitCheckpoint</c> this attempt's own real, verified source
    /// mutation produced — distinct from <see cref="AgentGitCheckpointId"/>, this attempt's
    /// immutable STARTING checkpoint, which is never overwritten by a result. Only ever set by
    /// <see cref="CompleteImplementation"/>, and only when its outcome is
    /// <see cref="Runs.AgentOutcome.Implemented"/>.</summary>
    public Guid? AgentResultGitCheckpointId { get; private set; }

    /// <summary>How this Agent attempt's provider child process actually ended, as measured by the
    /// host — process-level execution evidence, never a semantic classification (that is
    /// <see cref="AgentOutcome"/>). Recorded once, only by an Agent completion transition, in the
    /// same call as the terminal outcome, and only for a dispatched attempt whose provider process
    /// produced a real result. <see langword="null"/> means the evidence is truthfully absent:
    /// the attempt was never dispatched, the invocation failed before any process result existed,
    /// the host was interrupted before recording one, or the attempt predates this evidence.</summary>
    public ProcessOutcome? AgentProcessOutcome { get; private set; }

    /// <summary>Only set when <see cref="AgentProcessOutcome"/> is <see cref="Runs.ProcessOutcome.Exited"/> —
    /// mirrors <see cref="ProcessExitCode"/>.</summary>
    public int? AgentProcessExitCode { get; private set; }

    /// <summary>Non-negative host-measured duration of the provider child process. Set together
    /// with <see cref="AgentProcessOutcome"/>, never independently.</summary>
    public TimeSpan? AgentProcessDuration { get; private set; }

    /// <summary>Returns the recorded host-measured process evidence, or <see langword="null"/> when
    /// it is absent, its persisted shape is not valid evidence — an inconsistent row is reported
    /// as unknown rather than partially trusted — or the attempt has not yet concluded. A still-
    /// <see cref="AttemptStatus.Running"/> or never-dispatched (<see cref="AgentDispatchedAtUtc"/>
    /// is <see langword="null"/>) attempt always reports unknown process evidence here, even when
    /// its persisted row already carries seemingly well-formed process fields (for example a
    /// corrupted or prematurely populated row) — non-terminal evidence is never trusted by any
    /// caller of this method, mirroring <see cref="GetAgentTokenUsageEvidence"/>'s own rule. Non-Agent
    /// attempts never have this evidence.</summary>
    public AgentProcessExecutionEvidence? GetAgentProcessExecutionEvidence()
    {
        if (Kind != AttemptKind.Agent
            || Status == AttemptStatus.Running
            || AgentDispatchedAtUtc is null
            || AgentProcessOutcome is not { } outcome
            || AgentProcessDuration is not { } duration
            || AgentProcessExecutionEvidence.Validate(outcome, AgentProcessExitCode, duration) is not null)
        {
            return null;
        }

        return AgentProcessExecutionEvidence.Create(outcome, AgentProcessExitCode, duration);
    }

    /// <summary>Provider-reported input token count for this Agent attempt's invocation —
    /// best-effort observation, never host-measured and never required for any outcome. Recorded
    /// once, only by an Agent completion transition, in the same call as the terminal outcome, and
    /// only for a dispatched attempt whose provider reported usage through a proven contract.
    /// <see langword="null"/> means the evidence is truthfully absent: the attempt was never
    /// dispatched, its provider has no proven usage contract, the provider did not report usage,
    /// the host was interrupted before recording one, or the attempt predates this evidence.</summary>
    public int? AgentInputTokens { get; private set; }

    /// <summary>Provider-reported output token count. Set together with
    /// <see cref="AgentInputTokens"/>, never independently.</summary>
    public int? AgentOutputTokens { get; private set; }

    /// <summary>Provider-reported cache-creation input token count. Legitimately null even when
    /// <see cref="AgentInputTokens"/> is set, for a provider contract without a cache breakdown.</summary>
    public int? AgentCacheCreationInputTokens { get; private set; }

    /// <summary>Provider-reported cache-read input token count. Legitimately null even when
    /// <see cref="AgentInputTokens"/> is set, for a provider contract without a cache breakdown.</summary>
    public int? AgentCacheReadInputTokens { get; private set; }

    /// <summary>The adapter-owned parsing-contract tag that produced the recorded token usage —
    /// internal provenance only, never a UI-facing value. Set together with
    /// <see cref="AgentInputTokens"/>, never independently.</summary>
    public string? AgentTokenUsageSchemaVersion { get; private set; }

    /// <summary>Returns the recorded provider-reported token-usage evidence, or
    /// <see langword="null"/> when it is absent, its persisted shape is not valid evidence — an
    /// inconsistent row (for example a missing required member, a negative count, or an unsupported
    /// provider/schema pair) is reported as unknown rather than partially trusted — or the attempt
    /// has not yet concluded. A still-<see cref="AttemptStatus.Running"/> or never-dispatched
    /// (<see cref="AgentDispatchedAtUtc"/> is <see langword="null"/>) attempt always reports unknown
    /// usage here, even when its persisted row already carries seemingly well-formed token fields
    /// (for example a corrupted or prematurely populated row) — non-terminal evidence is never
    /// trusted by any caller of this method, mirroring the run-wide token-usage summary's own rule.
    /// Non-Agent attempts never have this evidence.</summary>
    public AgentTokenUsageEvidence? GetAgentTokenUsageEvidence() =>
        Kind != AttemptKind.Agent || Status == AttemptStatus.Running || AgentDispatchedAtUtc is null
            ? null
            : AgentTokenUsageEvidence.FromPersisted(
                AgentProvider, AgentInputTokens, AgentOutputTokens, AgentCacheCreationInputTokens,
                AgentCacheReadInputTokens, AgentTokenUsageSchemaVersion);

    /// <summary>The one canonical project-owned text of the model identifiers the provider listed for this Agent attempt's
    /// invocation with each one's reported context-window and maximum-output token limits (see
    /// <see cref="AgentModelContextLimitsEvidence.Serialize"/>). Recorded once, only by an Agent completion transition, in
    /// the same call as the terminal outcome. <see langword="null"/> means the evidence is truthfully absent: the attempt
    /// was never dispatched, its provider reported none in a proven contract, the host was interrupted before recording
    /// one, or the attempt predates this evidence. Read it only through <see cref="GetAgentModelContextLimitsEvidence"/>:
    /// stored text that is not exactly valid canonical evidence is unknown, never partially trusted.</summary>
    public string? AgentModelContextLimitsSnapshot { get; private set; }

    /// <summary>Returns the recorded provider-reported model context-limit evidence, or <see langword="null"/> when it is
    /// absent, the stored text is not canonical valid evidence for this attempt's provider (malformed, oversized, another
    /// version, source, or provider), or the attempt has not yet concluded. A still-<see cref="AttemptStatus.Running"/> or
    /// never-dispatched attempt always reports unknown evidence here, even when its row already carries well-formed text.
    /// Non-Agent attempts never have this evidence. Historical provider-reported observation only: never remaining
    /// context or next-invocation capacity.</summary>
    public AgentModelContextLimitsEvidence? GetAgentModelContextLimitsEvidence() =>
        Kind != AttemptKind.Agent || Status == AttemptStatus.Running || AgentDispatchedAtUtc is null
            ? null
            : AgentModelContextLimitsEvidence.FromPersisted(AgentProvider, AgentModelContextLimitsSnapshot);

    /// <summary>The one canonical project-owned text of the decision that stopped this never-dispatched Codex attempt because of the
    /// run's account-usage stop (see <see cref="AgentCodexAccountUsageDecision.Serialize"/>). Recorded once, only by
    /// <see cref="CompleteAgentAccountUsageStop"/>, in the same call as the terminal outcome. <see langword="null"/> means no
    /// decision was recorded, which is never read as "below the threshold". Read it only through
    /// <see cref="GetAgentAccountUsageDecision"/>: stored text that is not exactly valid canonical evidence is unknown.</summary>
    public string? AgentAccountUsageDecisionSnapshot { get; private set; }

    /// <summary>The recorded decision, or <see langword="null"/> when it is absent, the stored text is not canonical valid
    /// evidence for this Codex attempt, the attempt has not concluded, it was dispatched, or its outcome is not one of the two stop
    /// outcomes. Never throws.</summary>
    public AgentCodexAccountUsageDecision? GetAgentAccountUsageDecision()
    {
        if (Kind != AttemptKind.Agent || Status != AttemptStatus.Failed || AgentDispatchedAtUtc is not null
            || AgentOutcome is not (Runs.AgentOutcome.AccountUsageStopReached or Runs.AgentOutcome.AccountUsageEvidenceUnavailable))
        {
            return null;
        }

        // Canonical text alone does not prove this attempt's decision: it must agree with the attempt's own immutable threshold
        // snapshot, exactly as the recording transition required. An ordinary decision needs the actual valid, equal threshold;
        // the unusable-threshold decision needs an actually malformed snapshot; an absent snapshot confirms nothing.
        var decision = AgentCodexAccountUsageDecision.FromPersisted(AgentProvider, AgentAccountUsageDecisionSnapshot);
        return decision is not null && AgreesWithThresholdSnapshot(decision, ReadAgentCodexAccountUsageStopPercent()) ? decision : null;
    }

    private static bool AgreesWithThresholdSnapshot(AgentCodexAccountUsageDecision decision, CodexAccountUsageStopReading snapshot) =>
        !snapshot.IsAbsent
        && (decision.Reason == CodexAccountUsageDecisionReason.ThresholdUnusable
            ? snapshot.IsMalformed
            : !snapshot.IsMalformed && decision.ThresholdPercent == snapshot.Value);

    /// <summary>The terminal pre-dispatch transition of the account-usage stop: records the bounded decision and the matching outcome
    /// atomically for a Running, never-dispatched Codex attempt that took an account-usage stop snapshot. The decision must agree
    /// with that snapshot (its threshold equals a valid snapshot; an unusable-threshold decision needs a malformed one), and a
    /// second decision, a dispatched attempt or an attempt without a snapshot is refused before any mutation. No dispatch marker,
    /// process evidence, token usage or model limits exist for it.</summary>
    public void CompleteAgentAccountUsageStop(AgentCodexAccountUsageDecision decision, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (Kind != AttemptKind.Agent || AgentProvider != Runs.AgentProvider.Codex)
        {
            throw new InvalidOperationException("Only a Codex Agent attempt can record an account-usage stop.");
        }

        if (Status != AttemptStatus.Running || AgentDispatchedAtUtc.HasValue || AgentAccountUsageDecisionSnapshot is not null)
        {
            throw new InvalidOperationException("An account-usage stop is recorded once, for a claimed attempt that was never dispatched.");
        }

        var snapshot = ReadAgentCodexAccountUsageStopPercent();
        if (snapshot.IsAbsent)
        {
            throw new InvalidOperationException("This attempt did not claim an account-usage stop.");
        }

        if (!AgreesWithThresholdSnapshot(decision, snapshot))
        {
            throw new InvalidOperationException("The decision does not agree with the attempt's account-usage stop snapshot.");
        }

        AgentOutcome = decision.Kind == CodexAccountUsageDecisionKind.Reached
            ? Runs.AgentOutcome.AccountUsageStopReached
            : Runs.AgentOutcome.AccountUsageEvidenceUnavailable;
        AgentAccountUsageDecisionSnapshot = decision.Serialize();
        Status = AttemptStatus.Failed;
        CompletedAtUtc = nowUtc;
    }

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
    /// or <see cref="Runs.AgentOutcome.Challenged"/> for <see cref="Runs.AgentResponseContract.CriticalReview"/>;
    /// <see cref="Runs.AgentOutcome.Resolved"/> for <see cref="Runs.AgentResponseContract.ChallengeResolution"/> —
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
    /// <param name="processEvidence">Host-measured evidence of how the provider child process
    /// ended, recorded atomically with the outcome. Null when no process result exists — always the
    /// case for every pre-dispatch classification. See <see cref="AgentProcessEvidencePolicy"/>.</param>
    /// <param name="tokenUsage">Provider-reported token usage, recorded atomically with the outcome
    /// and process evidence. Null when the provider reported none through a proven contract. See
    /// <see cref="AgentTokenUsageEvidencePolicy"/>.</param>
    /// <param name="modelContextLimits">The model identifiers the provider listed with their reported context-window and
    /// maximum-output limits, recorded atomically with the outcome and independent of both it and the token usage. Null
    /// when the provider reported none through a proven contract. See <see cref="AgentModelContextLimitsEvidencePolicy"/>.</param>
    public void CompleteAgent(
        AgentOutcome outcome,
        string? completionFingerprintSha256,
        DateTimeOffset nowUtc,
        AgentProcessExecutionEvidence? processEvidence = null,
        AgentTokenUsageEvidence? tokenUsage = null,
        AgentModelContextLimitsEvidence? modelContextLimits = null)
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

        var contract = AgentAttemptContract.For(AgentResponseContract!.Value);

        // CompleteAgent remains the shared transition for every contract's pre-dispatch and generic
        // failure classifications (SourceChanged, WorkspaceNoLongerEligible,
        // CheckpointEvidenceUnavailable, and each contract's own "input already handled" outcome),
        // including a WorkspaceMutating contract — those callers always pass a null completion
        // fingerprint, so the override below never spuriously fires for them. What CompleteAgent
        // must never do is record a WorkspaceMutating contract's own real success outcome: only its
        // dedicated transition (CompleteImplementation or CompleteReviewCorrection) may skip the
        // override and record the resulting checkpoint identity.
        if (contract.Effect != AgentEffectKind.ReadOnly && contract.CompletedOutcomes.Contains(outcome))
        {
            throw new InvalidOperationException(
                $"{outcome} is this attempt's own contract's success outcome for a WorkspaceMutating response contract; it must " +
                "be recorded through that contract's dedicated completion transition, never CompleteAgent.");
        }

        var effectiveOutcome = completionFingerprintSha256 is not null
            && !string.Equals(completionFingerprintSha256, AgentCheckpointFingerprintSha256, StringComparison.Ordinal)
                ? Runs.AgentOutcome.SourceChanged
                : outcome;

        // A ReadOnly-role success outcome recognized by ANY contract, not necessarily this
        // attempt's own — an attempt still completes AttemptStatus.Completed only when the
        // outcome also belongs to its own contract's CompletedOutcomes, checked just below.
        var isRecognizedSuccessOutcome = AgentAttemptContract.CompletedOutcomesForEffect(AgentEffectKind.ReadOnly).Contains(effectiveOutcome);

        // Independent Domain-level backstop, never the only line of defense (the Application
        // boundary that records a provider result rejects both cases before ever reaching this
        // call) — a successful outcome is never observable for an attempt that was never actually
        // dispatched to the provider, or for one recorded without fresh evidence confirming the
        // checkpoint it claims to be about, or for one that does not match this attempt's own
        // response contract (e.g. a Proposal can never be recorded for a critical-review attempt,
        // and a Resolved can never be recorded for a planning or critical-review attempt).
        if (isRecognizedSuccessOutcome)
        {
            if (!AgentDispatchedAtUtc.HasValue)
            {
                throw new InvalidOperationException($"{effectiveOutcome} cannot be recorded for an attempt that was never dispatched.");
            }

            if (string.IsNullOrWhiteSpace(completionFingerprintSha256))
            {
                throw new InvalidOperationException($"{effectiveOutcome} cannot be recorded without fresh completion evidence.");
            }

            if (!contract.CompletedOutcomes.Contains(effectiveOutcome))
            {
                throw new InvalidOperationException($"{effectiveOutcome} is not a valid outcome for this attempt's response contract.");
            }
        }

        // Evaluated against the caller's requested outcome, not the drift-overridden one: a caller
        // may never claim a success (or InvalidStructuredOutput) without clean exit evidence, even
        // when a fingerprint mismatch then downgrades it to SourceChanged.
        EnsureAgentProcessEvidenceCanBeRecorded(outcome, processEvidence);
        EnsureAgentTokenUsageCanBeRecorded(outcome, tokenUsage);
        EnsureAgentModelContextLimitsCanBeRecorded(outcome, modelContextLimits);

        AgentOutcome = effectiveOutcome;
        ApplyAgentProcessEvidence(processEvidence);
        ApplyAgentTokenUsage(tokenUsage);
        ApplyAgentModelContextLimits(modelContextLimits);
        Status = isRecognizedSuccessOutcome ? AttemptStatus.Completed : AttemptStatus.Failed;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>
    /// Independent Domain backstop for host-measured process evidence, shared by every Agent
    /// completion transition so the rule cannot drift between them. Evidence is write-once,
    /// bound to a dispatched attempt, and required (as a clean exit) for every semantic success
    /// outcome, <see cref="Runs.AgentOutcome.InvalidStructuredOutput"/>, and every other
    /// classification that can only be reached once the provider process is already known to have
    /// exited cleanly — <see cref="Runs.AgentOutcome.NoChangesProduced"/>,
    /// <see cref="Runs.AgentOutcome.ImplementationHeadChanged"/>,
    /// <see cref="Runs.AgentOutcome.CorrectionNoChangesProduced"/>, and
    /// <see cref="Runs.AgentOutcome.CorrectionHeadChanged"/> — even though each of those four
    /// completes this attempt as <see cref="AttemptStatus.Failed"/>. See
    /// <see cref="AgentProcessEvidencePolicy"/> for the complete, closed rule. Always called before
    /// any mutation, so a violation leaves the attempt untouched.
    /// </summary>
    private void EnsureAgentProcessEvidenceCanBeRecorded(AgentOutcome requestedOutcome, AgentProcessExecutionEvidence? processEvidence)
    {
        if (processEvidence is not null && AgentProcessOutcome.HasValue)
        {
            throw new InvalidOperationException("Agent process evidence is write-once.");
        }

        var violation = AgentProcessEvidencePolicy.Evaluate(requestedOutcome, AgentDispatchedAtUtc.HasValue, processEvidence);
        if (violation is not null)
        {
            throw new InvalidOperationException($"{requestedOutcome} cannot be recorded with this process evidence: {violation}.");
        }
    }

    private void ApplyAgentProcessEvidence(AgentProcessExecutionEvidence? processEvidence)
    {
        if (processEvidence is null)
        {
            return;
        }

        AgentProcessOutcome = processEvidence.Outcome;
        AgentProcessExitCode = processEvidence.ExitCode;
        AgentProcessDuration = processEvidence.Duration;
    }

    /// <summary>
    /// Independent Domain backstop for provider-reported token usage, shared by every Agent
    /// completion transition so the rule cannot drift between them. Usage is write-once and bound
    /// to a dispatched attempt whose outcome is not a pre-invocation classification; unlike process
    /// evidence it is never required for any outcome. See <see cref="AgentTokenUsageEvidencePolicy"/>.
    /// Always called before any mutation, so a violation leaves the attempt untouched.
    /// </summary>
    private void EnsureAgentTokenUsageCanBeRecorded(AgentOutcome requestedOutcome, AgentTokenUsageEvidence? tokenUsage)
    {
        if (tokenUsage is not null && (AgentInputTokens.HasValue || AgentTokenUsageSchemaVersion is not null))
        {
            throw new InvalidOperationException("Agent token-usage evidence is write-once.");
        }

        var violation = AgentTokenUsageEvidencePolicy.Evaluate(AgentProvider, requestedOutcome, AgentDispatchedAtUtc.HasValue, tokenUsage);
        if (violation is not null)
        {
            throw new InvalidOperationException($"{requestedOutcome} cannot be recorded with this token-usage evidence: {violation}.");
        }
    }

    private void ApplyAgentTokenUsage(AgentTokenUsageEvidence? tokenUsage)
    {
        if (tokenUsage is null)
        {
            return;
        }

        AgentInputTokens = tokenUsage.InputTokens;
        AgentOutputTokens = tokenUsage.OutputTokens;
        AgentCacheCreationInputTokens = tokenUsage.CacheCreationInputTokens;
        AgentCacheReadInputTokens = tokenUsage.CacheReadInputTokens;
        AgentTokenUsageSchemaVersion = tokenUsage.SchemaVersion;
    }

    /// <summary>
    /// Independent Domain backstop for provider-reported model context limits, shared by every Agent completion
    /// transition so the rule cannot drift between them. The evidence is write-once and bound to a dispatched attempt
    /// of its own provider whose outcome is not a pre-invocation classification; like token usage it is never required
    /// for any outcome and is independent of it. See <see cref="AgentModelContextLimitsEvidencePolicy"/>. Always called
    /// before any mutation, so a violation leaves the attempt untouched.
    /// </summary>
    private void EnsureAgentModelContextLimitsCanBeRecorded(AgentOutcome requestedOutcome, AgentModelContextLimitsEvidence? modelContextLimits)
    {
        if (modelContextLimits is not null && AgentModelContextLimitsSnapshot is not null)
        {
            throw new InvalidOperationException("Agent model context-limit evidence is write-once.");
        }

        var violation = AgentModelContextLimitsEvidencePolicy.Evaluate(
            AgentProvider, requestedOutcome, AgentDispatchedAtUtc.HasValue, modelContextLimits);
        if (violation is not null)
        {
            throw new InvalidOperationException($"{requestedOutcome} cannot be recorded with this model context-limit evidence: {violation}.");
        }
    }

    private void ApplyAgentModelContextLimits(AgentModelContextLimitsEvidence? modelContextLimits)
    {
        if (modelContextLimits is not null)
        {
            AgentModelContextLimitsSnapshot = modelContextLimits.Serialize();
        }
    }

    /// <summary>
    /// The single atomic completion transition for a Claude Code implementation attempt —
    /// deliberately independent of <see cref="CompleteAgent"/> and its fingerprint-mismatch-to-
    /// <see cref="Runs.AgentOutcome.SourceChanged"/> override, which must never apply here: a
    /// real, successful implementation is EXPECTED and REQUIRED to change the workspace's
    /// fingerprint away from <see cref="AgentCheckpointFingerprintSha256"/> (the immutable
    /// starting checkpoint), so reusing that override would silently reclassify every genuine
    /// success as spurious drift. The caller (the recording Application handler) has already
    /// independently captured fresh Git evidence and decided <paramref name="outcome"/> before
    /// this call — this method only records that decision atomically, and is the only transition
    /// that may ever set <see cref="AgentResultGitCheckpointId"/>, and only for
    /// <see cref="Runs.AgentOutcome.Implemented"/>.
    /// </summary>
    public void CompleteImplementation(
        AgentOutcome outcome,
        Guid? resultGitCheckpointId,
        DateTimeOffset nowUtc,
        AgentProcessExecutionEvidence? processEvidence = null,
        AgentTokenUsageEvidence? tokenUsage = null,
        AgentModelContextLimitsEvidence? modelContextLimits = null)
    {
        if (Kind != AttemptKind.Agent || AgentResponseContract != Runs.AgentResponseContract.ImplementationReport)
        {
            throw new InvalidOperationException(
                "Only an Agent attempt with the ImplementationReport response contract can record an implementation outcome.");
        }

        // Resolve and verify the ImplementationReport contract itself — a static, always-true
        // invariant of AgentAttemptContract's own fixed table today, checked explicitly rather than
        // assumed, so a future change to that table can never silently make this transition
        // inconsistent with its own declared contract.
        var contract = AgentAttemptContract.For(Runs.AgentResponseContract.ImplementationReport);
        if (contract.Effect != AgentEffectKind.WorkspaceMutating || contract.Role != Runs.AgentRole.Implementer)
        {
            throw new InvalidOperationException("The ImplementationReport contract must be WorkspaceMutating with role Implementer.");
        }

        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot complete an attempt that is {Status}.");
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a defined agent outcome.");
        }

        var isSuccess = contract.CompletedOutcomes.Contains(outcome);

        // Independent Domain-level backstop, never the only line of defense — mirrors
        // CompleteAgent's own isSuccess-gated checks exactly, minus the fingerprint-override
        // this attempt shape must never apply.
        if (isSuccess)
        {
            if (!AgentDispatchedAtUtc.HasValue)
            {
                throw new InvalidOperationException($"{outcome} cannot be recorded for an attempt that was never dispatched.");
            }

            if (resultGitCheckpointId is null || resultGitCheckpointId == Guid.Empty)
            {
                throw new ArgumentException(
                    "An Implemented outcome requires the resulting checkpoint identity.", nameof(resultGitCheckpointId));
            }
        }
        else if (resultGitCheckpointId.HasValue)
        {
            throw new ArgumentException(
                "Only an Implemented outcome may carry a resulting checkpoint identity.", nameof(resultGitCheckpointId));
        }

        EnsureAgentProcessEvidenceCanBeRecorded(outcome, processEvidence);
        EnsureAgentTokenUsageCanBeRecorded(outcome, tokenUsage);
        EnsureAgentModelContextLimitsCanBeRecorded(outcome, modelContextLimits);

        AgentOutcome = outcome;
        ApplyAgentProcessEvidence(processEvidence);
        ApplyAgentTokenUsage(tokenUsage);
        ApplyAgentModelContextLimits(modelContextLimits);
        AgentResultGitCheckpointId = resultGitCheckpointId;
        Status = isSuccess ? AttemptStatus.Completed : AttemptStatus.Failed;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>
    /// The single atomic completion transition for an Implementer review-correction attempt —
    /// deliberately independent of <see cref="CompleteAgent"/> and its fingerprint-mismatch-to-
    /// <see cref="Runs.AgentOutcome.SourceChanged"/> override, which must never apply here: a
    /// real, successful correction is EXPECTED and REQUIRED to change the workspace's fingerprint
    /// away from <see cref="AgentCheckpointFingerprintSha256"/> (the immutable starting
    /// checkpoint), so reusing that override would silently reclassify every genuine success as
    /// spurious drift. The caller (the recording Application handler) has already independently
    /// captured fresh Git evidence and decided <paramref name="outcome"/> before this call — this
    /// method only records that decision atomically, and is the only transition that may ever set
    /// <see cref="AgentResultGitCheckpointId"/> for a ReviewCorrection attempt, and only for
    /// <see cref="Runs.AgentOutcome.CorrectionApplied"/>.
    /// </summary>
    public void CompleteReviewCorrection(
        AgentOutcome outcome,
        Guid? resultGitCheckpointId,
        DateTimeOffset nowUtc,
        AgentProcessExecutionEvidence? processEvidence = null,
        AgentTokenUsageEvidence? tokenUsage = null,
        AgentModelContextLimitsEvidence? modelContextLimits = null)
    {
        if (Kind != AttemptKind.Agent || AgentResponseContract != Runs.AgentResponseContract.ReviewCorrection)
        {
            throw new InvalidOperationException(
                "Only an Agent attempt with the ReviewCorrection response contract can record a review-correction outcome.");
        }

        // Resolve and verify the ReviewCorrection contract itself — a static, always-true invariant
        // of AgentAttemptContract's own fixed table today, checked explicitly rather than assumed.
        var contract = AgentAttemptContract.For(Runs.AgentResponseContract.ReviewCorrection);
        if (contract.Effect != AgentEffectKind.WorkspaceMutating || contract.Role != Runs.AgentRole.Implementer)
        {
            throw new InvalidOperationException("The ReviewCorrection contract must be WorkspaceMutating with role Implementer.");
        }

        if (Status != AttemptStatus.Running)
        {
            throw new InvalidOperationException($"Cannot complete an attempt that is {Status}.");
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a defined agent outcome.");
        }

        if (outcome is Runs.AgentOutcome.Proposed
            or Runs.AgentOutcome.Accepted
            or Runs.AgentOutcome.Challenged
            or Runs.AgentOutcome.Resolved
            or Runs.AgentOutcome.Implemented
            or Runs.AgentOutcome.ReviewApproved
            or Runs.AgentOutcome.ReviewChangesRequested
            or Runs.AgentOutcome.DiagnosisFindingsRecorded
            or Runs.AgentOutcome.DiagnosisEscalated)
        {
            throw new InvalidOperationException(
                $"{outcome} is not a valid terminal outcome for a ReviewCorrection attempt.");
        }

        var isSuccess = contract.CompletedOutcomes.Contains(outcome);

        // Independent Domain-level backstop — mirrors CompleteImplementation's own isSuccess-gated
        // checks exactly, minus the fingerprint-override this attempt shape must never apply.
        if (isSuccess)
        {
            if (!AgentDispatchedAtUtc.HasValue)
            {
                throw new InvalidOperationException($"{outcome} cannot be recorded for an attempt that was never dispatched.");
            }

            if (resultGitCheckpointId is null || resultGitCheckpointId == Guid.Empty)
            {
                throw new ArgumentException(
                    "A CorrectionApplied outcome requires the resulting checkpoint identity.", nameof(resultGitCheckpointId));
            }
        }
        else if (resultGitCheckpointId.HasValue)
        {
            throw new ArgumentException(
                "Only a CorrectionApplied outcome may carry a resulting checkpoint identity.", nameof(resultGitCheckpointId));
        }

        EnsureAgentProcessEvidenceCanBeRecorded(outcome, processEvidence);
        EnsureAgentTokenUsageCanBeRecorded(outcome, tokenUsage);
        EnsureAgentModelContextLimitsCanBeRecorded(outcome, modelContextLimits);

        AgentOutcome = outcome;
        ApplyAgentProcessEvidence(processEvidence);
        ApplyAgentTokenUsage(tokenUsage);
        ApplyAgentModelContextLimits(modelContextLimits);
        AgentResultGitCheckpointId = resultGitCheckpointId;
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
