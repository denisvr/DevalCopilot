using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

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
/// <para>
/// <see cref="LocalStep"/> is the internal identity of a coordinator-composed step of an explicitly authorized local execution
/// sequence (ADR-0060). It has an internal initializer, so no MVC request can supply it; an ordinary request is unaffected and, while a
/// sequence holds the run, is refused.
/// </para>
/// </summary>
public sealed record CreateImplementationAttemptCommand(Guid RunId, Guid PlanProposalMessageId, string? Guidance = null)
    : IManualTransactionCommand<Result<CreateImplementationAttemptCommandResult>>
{
    internal LocalExecutionStepTicket? LocalStep { get; init; }
}
