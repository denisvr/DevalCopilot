using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;

/// <summary>
/// The one explicit human decision to end an inactive manual Agent run as Abandoned (ADR-0031). The caller supplies only the human
/// reason; the host decides the mode, lifecycle, project, time and participants from fresh authority. Nothing is cancelled, repaired,
/// released or deleted, and no replacement run is created. Manual transaction: the handler owns the short write-locked transaction
/// that decides and records the transition and its one event together.
/// </summary>
public sealed record AbandonManualRunCommand(Guid RunId, string Reason)
    : IManualTransactionCommand<Result<AbandonManualRunCommandResult>>;
