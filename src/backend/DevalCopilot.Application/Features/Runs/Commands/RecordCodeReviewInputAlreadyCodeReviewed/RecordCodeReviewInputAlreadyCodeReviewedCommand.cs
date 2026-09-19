using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordCodeReviewInputAlreadyCodeReviewed;

/// <summary>
/// Records that an undispatched, Running Codex code-review attempt was superseded before dispatch:
/// another attempt already completed a successful (ReviewApproved or ReviewChangesRequested)
/// review of the exact same ExecutionReport-plus-verification-evidence input identity. Distinct
/// from <c>RecordAgentAttemptWorkspaceIneligibleCommand</c>: the run, workspace, lease, and
/// checkpoint all remain fully eligible here — only this input identity's applicability was lost.
/// Unlike the sibling bookkeeping commands, this handler never trusts the caller's own claim that a
/// competing review exists; it independently re-verifies that exact evidence itself and mutates
/// nothing if it does not hold. The provider is never invoked for this outcome. Mirrors
/// <c>RecordClaudeCriticalReviewInputAlreadyReviewedCommand</c>/<c>RecordChallengeResolutionInputAlreadyResolvedCommand</c>
/// exactly.
/// </summary>
public sealed record RecordCodeReviewInputAlreadyCodeReviewedCommand(Guid RunId, Guid AttemptId) : ICommand<Result>;
