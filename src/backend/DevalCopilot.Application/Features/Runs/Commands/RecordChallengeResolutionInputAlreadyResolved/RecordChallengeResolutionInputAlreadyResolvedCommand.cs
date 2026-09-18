using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionInputAlreadyResolved;

/// <summary>
/// Records that an undispatched, Running challenge-resolution attempt was superseded before
/// dispatch: another attempt already completed a successful resolution of the exact same
/// ordered input set (the original Proposal plus its complete Challenge set, in order).
/// Distinct from <c>RecordAgentAttemptWorkspaceIneligibleCommand</c>: the run, workspace, lease,
/// and checkpoint all remain fully eligible here — only this attempt's input identity was
/// superseded. Unlike the sibling bookkeeping commands, this handler never trusts the caller's
/// own claim that a competing resolution exists; it independently re-verifies that exact
/// evidence itself and mutates nothing if it does not hold. The provider is never invoked for
/// this outcome. Mirrors <c>RecordClaudeCriticalReviewInputAlreadyReviewedCommand</c> exactly.
/// </summary>
public sealed record RecordChallengeResolutionInputAlreadyResolvedCommand(Guid RunId, Guid AttemptId) : ICommand<Result>;
