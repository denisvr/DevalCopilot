using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewInputAlreadyReviewed;

/// <summary>
/// Records that an undispatched, Running Claude critical-review attempt was superseded before
/// dispatch: another attempt already completed a successful (Accepted or Challenged) review of
/// the exact same input Proposal. Distinct from
/// <c>RecordAgentAttemptWorkspaceIneligibleCommand</c>: the run, workspace, lease, and checkpoint
/// all remain fully eligible here — only the reviewed Proposal's applicability was lost. Unlike
/// the sibling bookkeeping commands, this handler never trusts the caller's own claim that a
/// competing review exists; it independently re-verifies that exact evidence itself and mutates
/// nothing if it does not hold. The provider is never invoked for this outcome.
/// </summary>
public sealed record RecordClaudeCriticalReviewInputAlreadyReviewedCommand(Guid RunId, Guid AttemptId) : ICommand<Result>;
