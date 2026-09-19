namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// A truthful, closed classification of how a terminal <see cref="AttemptKind.Agent"/> attempt
/// ended. Only <see cref="Proposed"/> represents a validated collaboration fact reaching the
/// ledger; every other value is a failure that preserves raw evidence as artifacts but appends no
/// <see cref="CollaborationMessage"/>.
/// </summary>
public enum AgentOutcome
{
    /// <summary>A protocol-1.0 Proposal was parsed, validated, and appended to the collaboration
    /// ledger.</summary>
    Proposed = 0,

    /// <summary>The workspace's source content changed — detected either before the provider was
    /// ever invoked or by a fresh recapture after it exited — so no Proposal can be trusted as
    /// evidence about the checkpoint this attempt committed to.</summary>
    SourceChanged = 1,

    /// <summary>The provider produced a final response, but it failed protocol/schema
    /// validation: malformed JSON, an unexpected shape, excessive cardinality or text, or missing
    /// required content. Raw evidence is preserved; no format-repair is attempted in this
    /// slice.</summary>
    InvalidStructuredOutput = 2,

    /// <summary>The provider could not be invoked or did not run to a usable result — a missing
    /// or invalid launch target, a process start failure, a non-zero exit, a timeout, or a
    /// cancellation. Never carries exception text or credentials; only this closed
    /// classification.</summary>
    ProviderInvocationFailed = 3,

    /// <summary>The provider ran and (as far as its process-level exit is concerned) succeeded,
    /// but fresh Git evidence could not be captured afterward, so there is no fingerprint to
    /// confirm the checkpoint is still current. A Proposal is never trusted without that
    /// confirmation, so this outcome is recorded instead of <see cref="Proposed"/> — never
    /// silently treated as success.</summary>
    CheckpointEvidenceUnavailable = 4,

    /// <summary>Detected immediately before dispatch: the Ready workspace, its active mutation
    /// lease, or the attempt's claimed checkpoint no longer holds. The provider is never invoked
    /// for this outcome — it is recorded instead of leaving the attempt to poll forever without
    /// ever becoming eligible again.</summary>
    WorkspaceNoLongerEligible = 5,

    /// <summary>A Claude critical-review attempt accepted the reviewed Proposal outright: exactly
    /// one <see cref="CollaborationMessageType.Acceptance"/> was parsed, validated, and appended
    /// to the collaboration ledger. Valid only for the ClaudeCode + CriticalReviewer + CriticalReview
    /// combination.</summary>
    Accepted = 6,

    /// <summary>A Claude critical-review attempt raised one or more material challenges against
    /// the reviewed Proposal: one to five <see cref="CollaborationMessageType.Challenge"/>
    /// messages were parsed, validated, and appended to the collaboration ledger. Valid only for
    /// the ClaudeCode + CriticalReviewer + CriticalReview combination.</summary>
    Challenged = 7,

    /// <summary>Detected immediately before dispatch, distinct from
    /// <see cref="WorkspaceNoLongerEligible"/>: the run/workspace/lease/checkpoint remain fully
    /// eligible, but another Claude critical-review attempt already completed a successful
    /// (<see cref="Accepted"/> or <see cref="Challenged"/>) review of the exact same input
    /// Proposal in the meantime. The provider is never invoked for this outcome. Recorded only by
    /// a dedicated command that independently re-verifies the competing review exists before ever
    /// mutating this attempt — never inferred or trusted from a caller's own claim. Valid only for
    /// the ClaudeCode + CriticalReviewer + CriticalReview combination.</summary>
    InputAlreadyReviewed = 8,

    /// <summary>A Codex challenge-resolution attempt explicitly resolved every input Challenge and
    /// emitted one revised Proposal: one <see cref="CollaborationMessageType.Decision"/> per
    /// Challenge plus exactly one revised <see cref="CollaborationMessageType.Proposal"/>, all
    /// parsed, validated, and appended to the collaboration ledger atomically. Valid only for the
    /// Codex + Resolver + ChallengeResolution combination.</summary>
    Resolved = 9,

    /// <summary>Detected immediately before dispatch, distinct from
    /// <see cref="WorkspaceNoLongerEligible"/>: the run/workspace/lease/checkpoint remain fully
    /// eligible, but another challenge-resolution attempt already completed a successful
    /// (<see cref="Resolved"/>) resolution of the exact same ordered input set — the original
    /// Proposal plus its complete, ordered Challenge set — in the meantime. The provider is never
    /// invoked for this outcome. Recorded only by a dedicated command that independently
    /// re-verifies the competing resolution exists before ever mutating this attempt — never
    /// inferred or trusted from a caller's own claim. Mirrors
    /// <see cref="InputAlreadyReviewed"/> exactly, one level further down the collaboration
    /// protocol. Valid only for the Codex + Resolver + ChallengeResolution combination.</summary>
    InputAlreadyResolved = 10,

    /// <summary>A Claude Code implementation attempt produced a real, verified source change:
    /// the process exited successfully, the structured ImplementationReport passed protocol
    /// validation, the freshly observed Git fingerprint genuinely differs from the immutable
    /// starting checkpoint, and the report's own changed-path set exactly matches the
    /// independently observed Git changed-path set. A new immutable <c>GitCheckpoint</c> is
    /// recorded and referenced by <see cref="Attempt.AgentResultGitCheckpointId"/>. Never
    /// recorded through <see cref="Attempt.CompleteAgent"/> — <see cref="Attempt.CompleteImplementation"/>
    /// is the only transition that may ever produce this outcome, deliberately without
    /// <see cref="Attempt.CompleteAgent"/>'s own fingerprint-mismatch-to-<see cref="SourceChanged"/>
    /// override, since a changed fingerprint is this outcome's own expected, required evidence
    /// of success, never drift. Valid only for the ClaudeCode + Implementer +
    /// ImplementationReport combination.</summary>
    Implemented = 11,

    /// <summary>A Claude Code implementation attempt's process exited successfully and produced
    /// a structurally valid ImplementationReport, but the freshly observed Git fingerprint is
    /// unchanged from the immutable starting checkpoint — no source mutation actually occurred.
    /// Never treated as a provider or evidence failure: the process and structured output were
    /// both genuinely valid: there was simply nothing to implement, or the provider concluded no
    /// change was required. No <c>GitCheckpoint</c> is recorded; the workspace remains Ready.
    /// Valid only for the ClaudeCode + Implementer + ImplementationReport combination.</summary>
    NoChangesProduced = 12,

    /// <summary>Detected immediately before dispatch, distinct from
    /// <see cref="WorkspaceNoLongerEligible"/>: the run/workspace/lease/checkpoint remain fully
    /// eligible, but another implementation attempt already completed a successful
    /// (<see cref="Implemented"/>) implementation of the exact same resolved plan and starting
    /// checkpoint in the meantime. The provider is never invoked for this outcome. Recorded only
    /// by a dedicated command that independently re-verifies the competing implementation exists
    /// before ever mutating this attempt — never inferred or trusted from a caller's own claim.
    /// Mirrors <see cref="InputAlreadyResolved"/> exactly, one stage further down the
    /// collaboration protocol. Valid only for the ClaudeCode + Implementer +
    /// ImplementationReport combination.</summary>
    InputAlreadyImplemented = 13,

    /// <summary>A Claude Code implementation attempt's process exited successfully, but the
    /// freshly observed HEAD commit SHA no longer matches the immutable starting checkpoint's own
    /// HEAD commit SHA. Claude's implementation tool allowlist never includes Git or any process
    /// tool, so it can never move HEAD itself — a changed HEAD is proof of an external or
    /// unauthorized mutation of the worktree during this attempt, never a trustworthy
    /// implementation, regardless of whether the report's own claimed changed-path set happens to
    /// match observed evidence. No <c>GitCheckpoint</c> is recorded and no ExecutionReport is
    /// appended; the workspace is always flagged NeedsAttention. Valid only for the ClaudeCode +
    /// Implementer + ImplementationReport combination.</summary>
    ImplementationHeadChanged = 14,

    /// <summary>A Codex implementation-review attempt approved the reviewed Execution report
    /// outright: exactly one <see cref="CollaborationMessageType.ReviewApproval"/> was parsed,
    /// validated, and appended to the collaboration ledger, alongside one immutable
    /// <see cref="CheckpointReview"/> fact recording the complete verification evidence set every
    /// enabled command's Passed execution formed. Valid only for the Codex + CodeReviewer +
    /// ImplementationReview combination.</summary>
    ReviewApproved = 15,

    /// <summary>A Codex implementation-review attempt raised one or more structured findings
    /// against the reviewed Execution report: one to ten <see cref="CollaborationMessageType.ReviewFinding"/>
    /// messages were parsed, validated, and appended to the collaboration ledger, alongside one
    /// immutable <see cref="CheckpointReview"/> fact recording the complete verification evidence
    /// set. Valid only for the Codex + CodeReviewer + ImplementationReview combination.</summary>
    ReviewChangesRequested = 16,

    /// <summary>Detected immediately before dispatch, distinct from
    /// <see cref="WorkspaceNoLongerEligible"/>: the run/workspace/lease/checkpoint remain fully
    /// eligible, but another code-review attempt already completed a successful
    /// (<see cref="ReviewApproved"/> or <see cref="ReviewChangesRequested"/>) review of the exact
    /// same Execution report, result checkpoint, and ordered verification-execution set in the
    /// meantime. The provider is never invoked for this outcome. Recorded only by a dedicated
    /// command that independently re-verifies the competing review exists before ever mutating
    /// this attempt — never inferred or trusted from a caller's own claim. Mirrors
    /// <see cref="InputAlreadyImplemented"/> exactly, one stage further down the collaboration
    /// protocol. Valid only for the Codex + CodeReviewer + ImplementationReview
    /// combination.</summary>
    InputAlreadyCodeReviewed = 17,
}
