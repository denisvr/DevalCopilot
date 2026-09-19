using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationInputAlreadyImplemented;

/// <summary>
/// Records that an undispatched, Running implementation attempt was superseded before dispatch:
/// another attempt already completed a successful implementation of the exact same resolved
/// plan (identified by its sequence-0 Proposal message id) against the exact same starting
/// checkpoint. Distinct from <c>RecordAgentAttemptWorkspaceIneligibleCommand</c>: the run,
/// workspace, lease, and checkpoint all remain fully eligible here — only this attempt's plan
/// identity was superseded. Unlike the sibling bookkeeping commands, this handler never trusts
/// the caller's own claim that a competing implementation exists; it independently re-verifies
/// that exact evidence itself and mutates nothing if it does not hold. The provider is never
/// invoked for this outcome. Mirrors <c>RecordChallengeResolutionInputAlreadyResolvedCommand</c>
/// exactly.
/// </summary>
public sealed record RecordImplementationInputAlreadyImplementedCommand(Guid RunId, Guid AttemptId) : ICommand<Result>;
