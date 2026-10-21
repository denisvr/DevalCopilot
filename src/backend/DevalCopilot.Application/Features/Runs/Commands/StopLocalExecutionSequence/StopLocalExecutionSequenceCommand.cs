using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.StopLocalExecutionSequence;

/// <summary>
/// The Human decision "Stop after current operation" for one exact local execution sequence of one exact run (ADR-0060). Before a
/// step's claim commits it revokes that step's preparation authority, so no operation, budget or grant is consumed; after the claim
/// committed the one claimed operation may still dispatch and finish (subject to every independent gate and its deadline) and no
/// successor is allowed. It is not process cancellation, run Stop or Pause. Repeated requests are idempotent and a sequence that
/// already ended is returned unchanged. Manual transaction: the handler owns the short write-locked transaction.
/// </summary>
public sealed record StopLocalExecutionSequenceCommand(Guid RunId, Guid SequenceId)
    : IManualTransactionCommand<Result<StopLocalExecutionSequenceCommandResult>>;
