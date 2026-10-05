using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageStop;

/// <summary>
/// Sets or clears the owner's optional, run-scoped Codex account-usage stop (ADR-0025): a used-percent threshold from 1 through 100
/// enforced on this run's later Codex claims and again before their invocation. <see cref="Percent"/> <see langword="null"/> disables
/// it. It applies to later claims only: never to an attempt already claimed, which keeps its own immutable snapshot, and never to
/// already dispatched work. It is a local guard over a provider-reported percentage, never account access, readiness, remaining
/// quota or a reservation. Editable only for a Created or Running run whose execution mode admits Agent work.
///
/// <para>
/// Manual transaction: the handler's single <c>SaveChangesAsync</c> owns the implicit database transaction that commits the Run
/// change and its event together, so a failed concurrency-checked UPDATE can never leave the queued event behind.
/// </para>
/// </summary>
public sealed record SetCodexAccountUsageStopCommand(Guid RunId, int? Percent)
    : IManualTransactionCommand<Result<SetCodexAccountUsageStopCommandResult>>;
