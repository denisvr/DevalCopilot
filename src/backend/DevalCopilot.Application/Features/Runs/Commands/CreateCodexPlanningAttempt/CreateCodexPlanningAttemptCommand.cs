using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;

/// <summary>
/// Creates one durable Codex planning attempt for an eligible run. Manual transaction: this
/// handler performs a fresh, bounded Git evidence capture and writes the sealed context-manifest
/// artifact, neither of which may run inside the mediator's ambient EF transaction.
/// </summary>
public sealed record CreateCodexPlanningAttemptCommand(Guid RunId)
    : IManualTransactionCommand<Result<CreateCodexPlanningAttemptCommandResult>>;
