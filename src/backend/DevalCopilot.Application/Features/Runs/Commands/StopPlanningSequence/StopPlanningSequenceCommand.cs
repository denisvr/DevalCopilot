using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.StopPlanningSequence;

/// <summary>
/// The Human decision "Stop after current step" for one exact planning sequence of one exact run (ADR-0055). Before a step's claim
/// commits it revokes that step's preparation authority, so no attempt and no budget is consumed; after the claim committed the already
/// claimed read-only step may still dispatch and finish and no successor is allowed. It is not process cancellation, run Stop or Pause.
/// Repeated requests are idempotent and a sequence that already ended is returned unchanged. Manual transaction: the handler owns the
/// short write-locked transaction that decides and records the transition and its events together.
/// </summary>
public sealed record StopPlanningSequenceCommand(Guid RunId, Guid SequenceId)
    : IManualTransactionCommand<Result<StopPlanningSequenceCommandResult>>;
