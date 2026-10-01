using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;

/// <summary>
/// Records the explicit human authorization of exactly one implementation claim for the final plan of a completed
/// second challenge-resolution round (ADR-0016). The final Proposal is derived from the persisted escalation, never from
/// the caller; <paramref name="Rationale"/> is the only caller-supplied value and is validated by the deterministic
/// <c>PlanningImplementationInstruction.Normalize</c> before any read. Manual transaction: the handler captures bounded
/// Git evidence, which may not run inside the mediator's ambient transaction, and then opens its own short one.
/// </summary>
public sealed record AuthorizePlanningImplementationCommand(Guid RunId, Guid EscalationMessageId, string Rationale)
    : IManualTransactionCommand<Result<AuthorizePlanningImplementationCommandResult>>;
