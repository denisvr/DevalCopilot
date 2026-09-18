using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptCheckpointEvidenceUnavailable;

/// <summary>
/// Records that fresh Git evidence could not be captured before an Agent attempt was ever
/// dispatched — either the capture itself reported a failure, or the evidence adapter threw.
/// Resolves the attempt to a truthful terminal outcome instead of leaving it Running and
/// undispatched, which would otherwise make it eligible again on every subsequent poll and
/// silently retry the same failing capture forever. The provider is never invoked for this
/// outcome. Distinct from <c>RecordAgentAttemptSourceChangedCommand</c> (evidence was captured but
/// disagreed with the claimed checkpoint) and <c>RecordAgentAttemptWorkspaceIneligibleCommand</c>
/// (workspace/lease/checkpoint state itself changed) — this is specifically "evidence could not be
/// captured at all."
/// </summary>
public sealed record RecordAgentAttemptCheckpointEvidenceUnavailableCommand(Guid RunId, Guid AttemptId) : ICommand<Result>;
