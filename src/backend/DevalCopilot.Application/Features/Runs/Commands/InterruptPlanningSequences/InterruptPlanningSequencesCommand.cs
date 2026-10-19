using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.InterruptPlanningSequences;

/// <summary>
/// Startup recovery of planning sequences (ADR-0055): every sequence that could still advance or drain (Armed, a preparation-only
/// session, a claimed step awaiting its result, or Stopping) becomes Interrupted before any hosted coordinator or provider supervisor
/// starts. A Reserved step is revoked (its claim cannot have committed without its link) or, when an attempt with its reserved identity
/// unexpectedly exists, marked unresolved; no attempt is created, dispatched, adopted or resumed here, and ordinary attempt recovery
/// stays authoritative for attempts. Nothing ever resumes an Interrupted sequence. Manual transaction.
/// </summary>
public sealed record InterruptPlanningSequencesCommand : IManualTransactionCommand<Result<InterruptPlanningSequencesCommandResult>>;
