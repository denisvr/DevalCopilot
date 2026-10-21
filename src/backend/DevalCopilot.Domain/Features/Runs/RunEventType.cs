namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The bounded deterministic event journal for the walking-skeleton run. Typed protocol
/// envelopes supplement these operational facts; the string values are durable schema, not UI labels.
/// </summary>
public static class RunEventType
{
    public const string RunStarted = "run.started";
    public const string CodexProposal = "codex.proposal";
    public const string ClaudeChallenge = "claude.challenge";
    public const string CodexResolution = "codex.resolution";
    public const string ClaudeExecution = "claude.execution";
    public const string RunCompleted = "run.completed";

    /// <summary>Metadata-only journal fact that a bounded collaboration envelope was recorded.</summary>
    public const string CollaborationMessageRecorded = "collaboration.message_recorded";

    /// <summary>Metadata-only fact: an <see cref="Artifact"/> was durably recorded for an
    /// attempt. The payload carries the artifact id, purpose, byte length, truncation flag, and
    /// capture outcome — never captured content.</summary>
    public const string ProcessOutputCaptured = "process.output_captured";

    /// <summary>Metadata-only fact: a Process attempt reached a terminal state with no artifact
    /// ever recorded — the adapter failed before a child process or output sink existed, so
    /// there was never anything to seal or capture. The payload carries only the attempt's
    /// final status; never exception text, arguments, environment values, or output.</summary>
    public const string ProcessEndedWithoutOutput = "process.ended_without_output";

    /// <summary>Metadata-only fact: a Codex planning Agent attempt was durably committed to
    /// being invoked. The payload carries only the attempt id and timestamp.</summary>
    public const string AgentAttemptDispatched = "agent.attempt_dispatched";

    /// <summary>Metadata-only fact: an Agent attempt reached a terminal state. The payload
    /// carries the attempt's final status and closed <see cref="AgentOutcome"/>; never raw
    /// provider output, exception text, or credentials.</summary>
    public const string AgentAttemptCompleted = "agent.attempt_completed";

    /// <summary>Metadata-only fact: a partial Agent-attempt output artifact left behind by a host
    /// interruption was imported by restart recovery. Distinct from
    /// <see cref="AgentAttemptCompleted"/> — the owning attempt is still <c>Running</c> when this
    /// is recorded, and recovering one artifact is never itself a terminal result.</summary>
    public const string AgentOutputRecovered = "agent.output_recovered";

    /// <summary>Metadata-only fact: the owner explicitly set or cleared the run-scoped requested
    /// Codex model/reasoning-effort for future Planner, Challenge Resolver, and Code Reviewer
    /// claims. The payload carries only the new requested model and effort (or both null when
    /// cleared) — never a catalog payload, credential, or diagnostic.</summary>
    public const string CodexAssignmentPreferenceChanged = "run.codex_assignment_preference_changed";

    /// <summary>The owner set or cleared the run-scoped Claude model-alias request for future
    /// CriticalReviewer, Implementer, and ReviewCorrection claims. The payload carries only the new
    /// requested alias (or null when cleared).</summary>
    public const string ClaudeModelPreferenceChanged = "run.claude_model_preference_changed";

    /// <summary>The owner set or cleared the run-scoped Claude agentic-turn-limit request for future
    /// initial implementation and review correction claims. The payload carries only the new requested
    /// limit (or null when cleared).</summary>
    public const string ClaudeMutationTurnLimitChanged = "run.claude_mutation_turn_limit_changed";

    /// <summary>The owner set or cleared one provider's advisory token-activity warning threshold.
    /// The payload carries only the provider name and the new threshold (null when cleared).</summary>
    public const string TokenWarningThresholdChanged = "run.token_warning_threshold_changed";

    /// <summary>The owner set or cleared one provider's token-activity stop threshold, which gates
    /// future Agent claims for that provider. The payload carries only the provider name and the new
    /// threshold (null when cleared).</summary>
    public const string TokenStopThresholdChanged = "run.token_stop_threshold_changed";

    /// <summary>The owner set or cleared the run-scoped Codex account-usage stop percentage, enforced on future Codex claims and
    /// their dispatch. The payload carries only the new percentage (null when cleared).</summary>
    public const string CodexAccountUsageStopChanged = "run.codex_account_usage_stop_changed";

    /// <summary>The owner set or cleared the run-scoped advisory Codex account-usage warning percentage (ADR-0026). The payload carries
    /// only the new percentage (null when cleared); the event never records an observation.</summary>
    public const string CodexAccountUsageWarningChanged = "run.codex_account_usage_warning_changed";

    /// <summary>A human-requested local commit was durably admitted and its workspace reserved (ADR-0029). The payload carries
    /// the operation, checkpoint and recorded parent/tree/commit identities, never message text or a path.</summary>
    public const string LocalCommitAdmitted = "local_commit.admitted";

    /// <summary>The single-use execution marker was recorded: the host is about to move the owned branch.</summary>
    public const string LocalCommitExecuting = "local_commit.executing";

    /// <summary>The recorded commit, tree, branch and index were proven; the run is completed by the same save.</summary>
    public const string LocalCommitCompleted = "local_commit.completed";

    /// <summary>The operation definitely did not promote; the run is failed by the same save.</summary>
    public const string LocalCommitFailed = "local_commit.failed";

    /// <summary>A startup interruption was proven unpromoted; the run is interrupted by the same save.</summary>
    public const string LocalCommitInterrupted = "local_commit.interrupted";

    /// <summary>The outcome is ambiguous; nothing is retried and the run stays nonterminal.</summary>
    public const string LocalCommitNeedsAttention = "local_commit.needs_attention";

    /// <summary>A human explicitly abandoned an inactive manual Agent run (ADR-0031). The payload carries only the normalized human
    /// reason; the one event is saved atomically with the terminal transition and it never records cancellation, repair or deletion.</summary>
    public const string RunAbandoned = "run.abandoned";

    /// <summary>A Human explicitly recorded the immutable GitHub observation scope of a completed local delivery (ADR-0040). The
    /// payload is the bounded canonical version-1 form of the target; it never records remote verification, permission, CI or authority.</summary>
    public const string GitHubObservationTargetRecorded = "run.github_observation_target_recorded";

    /// <summary>A Human explicitly confirmed, after a fresh host observation agreed, the GitHub repository node identifier of an ADR-0040
    /// target (ADR-0043). The payload is the bounded canonical version-1 form of the binding; it is historical identity evidence only and
    /// never records permission, branch or commit proof, CI or publication authority.</summary>
    public const string GitHubRepositoryIdentityBound = "run.github_repository_identity_bound";

    /// <summary>A Human explicitly approved ONE future normal push of the exact recorded local commit to its recorded owned branch in the
    /// repository named by a coherent ADR-0040 target and ADR-0043 binding (ADR-0048). The payload is the bounded canonical version-1 form
    /// of the approval snapshot; it records consent only and never an executed push, remote observation, permission or any other
    /// publication.</summary>
    public const string GitHubBranchPublicationApproved = "run.github_branch_publication_approved";

    /// <summary>A Human armed one explicitly authorized read-only planning sequence for the run (ADR-0055). The payload carries only the
    /// sequence identity and the exact pinned workspace, checkpoint and fingerprint; arming invoked nothing and consumed no budget.</summary>
    public const string PlanningSequenceArmed = "planning_sequence.armed";

    /// <summary>The coordinator reserved the one preparation intent of the next step. The payload carries identities and the ordinal
    /// only; a reservation is not a claim and consumes no budget.</summary>
    public const string PlanningSequenceStepReserved = "planning_sequence.step_reserved";

    /// <summary>The reserved step's attempt, inputs, artifact metadata and budget consumption committed atomically with its link. A claim
    /// is not proof that a provider process is running.</summary>
    public const string PlanningSequenceStepClaimed = "planning_sequence.step_claimed";

    /// <summary>The coordinator classified the terminal result of a claimed step.</summary>
    public const string PlanningSequenceStepConcluded = "planning_sequence.step_concluded";

    /// <summary>The Human chose Stop after current step. This is not process cancellation, run Stop or Pause.</summary>
    public const string PlanningSequenceStopRequested = "planning_sequence.stop_requested";

    /// <summary>The sequence ended in a terminal, attention or interrupted state with a fixed reason code.</summary>
    public const string PlanningSequenceEnded = "planning_sequence.ended";

    /// <summary>A Human armed one explicitly authorized local execution sequence for the run (ADR-0060). The payload carries identities and
    /// the pinned tuple only; arming invoked nothing, claimed nothing and consumed no budget.</summary>
    public const string LocalExecutionSequenceArmed = "local_execution_sequence.armed";

    /// <summary>The coordinator reserved the one preparation intent of the next step (identities and kind only). Not a claim.</summary>
    public const string LocalExecutionSequenceStepReserved = "local_execution_sequence.step_reserved";

    /// <summary>The reserved step's operation, inputs, artifacts, counters, budget and grant consumption committed atomically with its link.</summary>
    public const string LocalExecutionSequenceStepClaimed = "local_execution_sequence.step_claimed";

    /// <summary>The coordinator classified the terminal result of a claimed step.</summary>
    public const string LocalExecutionSequenceStepConcluded = "local_execution_sequence.step_concluded";

    /// <summary>The Human chose Stop after current operation. This is not process cancellation, run Stop or Pause.</summary>
    public const string LocalExecutionSequenceStopRequested = "local_execution_sequence.stop_requested";

    /// <summary>The sequence ended in a terminal, attention or interrupted state with a fixed reason code.</summary>
    public const string LocalExecutionSequenceEnded = "local_execution_sequence.ended";

    /// <summary>The host registered its own observation of one already claimed Agent attempt in Preparing (ADR-0056). The payload carries
    /// identities and the phase only. It claims, dispatches or starts nothing and proves no provider progress or live process.</summary>
    public const string AgentSupervisionRegistered = "agent_supervision.registered";

    /// <summary>The host observer reported entering a later observation phase with its fixed absolute deadline (ADR-0056). The payload
    /// carries identities and the phase only. Entering AwaitingAdapterResult does not show that a process started; heartbeats append no
    /// event.</summary>
    public const string AgentSupervisionPhaseEntered = "agent_supervision.phase_entered";

    /// <summary>The host's own observation of an Agent attempt recorded its first attention (ADR-0056): a stale heartbeat, an elapsed
    /// phase deadline, an observer that ended without a durable terminal result, or a host interruption. The payload carries identities,
    /// the phase and the fixed reason only. It proves neither that earlier authorized work stopped nor any provider progress.</summary>
    public const string AgentSupervisionAttention = "agent_supervision.attention";

    /// <summary>The host closed its observation of an Agent attempt (ADR-0056), with the fixed closure reason: the attempt reached its
    /// recorded terminal result, the observer ended without one, or the host was interrupted. Every actual closure appends exactly one.
    /// A closure never terminalizes the Attempt.</summary>
    public const string AgentSupervisionClosed = "agent_supervision.closed";
}
