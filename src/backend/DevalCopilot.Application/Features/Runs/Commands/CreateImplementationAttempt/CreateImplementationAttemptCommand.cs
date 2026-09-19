using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;

/// <summary>
/// Creates one durable Claude implementation attempt for an eligible run, implementing exactly
/// one authoritative resolved plan — identified only by its Proposal message id, never by
/// display text. <see cref="PlanProposalMessageId"/> must be either an original,
/// provider-observed Codex Proposal with a completed successful Claude Accepted review, or the
/// provider-observed revised Proposal emitted by a completed successful Codex Resolver attempt;
/// this handler independently determines which form applies (or rejects the request) and never
/// reconstructs eligibility from anything the caller merely asserts. Manual transaction: this
/// handler performs a fresh, bounded Git evidence capture and writes the sealed context-manifest
/// artifact, neither of which may run inside the mediator's ambient EF transaction — mirrors
/// <c>CreateChallengeResolutionAttemptCommand</c> exactly.
/// </summary>
public sealed record CreateImplementationAttemptCommand(Guid RunId, Guid PlanProposalMessageId)
    : IManualTransactionCommand<Result<CreateImplementationAttemptCommandResult>>;
