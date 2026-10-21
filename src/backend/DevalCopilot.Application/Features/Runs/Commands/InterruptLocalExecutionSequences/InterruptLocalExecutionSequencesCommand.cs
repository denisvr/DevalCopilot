using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.InterruptLocalExecutionSequences;

/// <summary>
/// Startup closure of local execution sequences (ADR-0060), run before any artifact recovery, attempt interruption, workspace
/// reconciliation or supervisor starts. Every sequence that could still advance or drain (Armed, a preparation-only session, a claimed
/// step awaiting its result, or Stopping) becomes Interrupted and its unclaimed preparations are revoked (or marked unresolved when an
/// operation with the reserved identity unexpectedly exists). A linked claim that was proved never dispatched is closed with the
/// dedicated dispatch refusal without invoking its adapter. A linked dispatched operation without a coherent recorded terminal result
/// is left exactly as recorded: nothing is interrupted, imported, terminalized or released, and nothing is ever resumed, redispatched
/// or adopted. Manual transaction.
/// </summary>
public sealed record InterruptLocalExecutionSequencesCommand
    : IManualTransactionCommand<Result<InterruptLocalExecutionSequencesCommandResult>>;
