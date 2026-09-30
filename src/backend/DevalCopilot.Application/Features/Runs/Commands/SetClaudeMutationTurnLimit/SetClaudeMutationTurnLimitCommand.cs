using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetClaudeMutationTurnLimit;

/// <summary>
/// Sets or clears the owner's explicit, run-scoped requested Claude agentic-turn limit for this run's
/// later initial implementation and review correction claims. <see cref="MaxTurns"/>
/// <see langword="null"/> clears the request (no <c>--max-turns</c> override); a non-null value must be
/// a whole number from 1 through 100. This is a request for a provider-loop guardrail only: never a
/// measured turn count, a token, cost, or account ceiling, a host-enforced limit, or a change to any
/// already-claimed attempt's own immutable snapshot.
///
/// <para>
/// Manual transaction: the handler's single <c>SaveChangesAsync</c> must own the implicit database
/// transaction that commits the Run change and its event together. Under the mediator's ambient
/// transaction a failed concurrency-checked UPDATE could leave the already-executed event INSERT in
/// that outer transaction.
/// </para>
/// </summary>
public sealed record SetClaudeMutationTurnLimitCommand(Guid RunId, int? MaxTurns)
    : IManualTransactionCommand<Result<SetClaudeMutationTurnLimitCommandResult>>;
