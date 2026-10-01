using DevalCopilot.Application.Features.Runs.Policies;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;

/// <summary>The bounded projection the implementation supervisor needs to revalidate, dispatch,
/// and invoke one implementation attempt — never a prompt, transcript, or credential. Mirrors
/// <c>EligibleChallengeResolutionAttempt</c>, minus any input-message identity: unlike a
/// challenge-resolution response, an implementation response never needs to be validated against
/// an expected message-id set, so the supervisor and
/// <c>RecordImplementationResultCommandHandler</c> each independently re-derive the plan Proposal
/// id from <c>ImplementationInputIdentity</c> only when they actually need it, rather than this
/// projection carrying it speculatively. <c>PlanningAuthorization</c> is the bounded fact of the human plan authorization
/// the attempt consumed (ADR-0016), proven from a fresh snapshot by the feed; it is carried only so the dispatch gate and
/// the adapter can check the sealed manifest against it.</summary>
public sealed record EligibleImplementationAttempt(
    Guid AttemptId,
    Guid RunId,
    Guid GitWorkspaceId,
    string WorkspacePath,
    Guid GitCheckpointId,
    string CheckpointFingerprintSha256,
    string ContextManifestRelativeStoragePath,
    long ContextManifestByteLength,
    string ContextManifestContentHash,
    TimeSpan Timeout,
    int MaxBytesPerStream,
    int MaxTotalCapturedBytes,
    string? RequestedClaudeModel = null,
    string? RequestedClaudeEffort = null,
    int? RequestedMaxTurns = null,
    string? AdapterContractVersion = null,
    string? DirectHumanGuidance = null,
    PlanningImplementationAuthorizationFact? PlanningAuthorization = null);
