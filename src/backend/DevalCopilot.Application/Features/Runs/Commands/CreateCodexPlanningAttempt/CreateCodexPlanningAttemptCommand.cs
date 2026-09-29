using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;

/// <summary>
/// Creates one durable Codex planning attempt for an eligible run. Manual transaction: this
/// handler performs a fresh, bounded Git evidence capture and writes the sealed context-manifest
/// artifact, neither of which may run inside the mediator's ambient EF transaction.
/// <paramref name="RepairSourceAttemptId"/> is <see langword="null"/> for an ordinary planning
/// request. When set, this is the one human-requested format repair of that exact source attempt:
/// the same claim with the same protections, plus source-eligibility checks at the request and again
/// at the durable claim boundary. See <see cref="PlanningRepairSource"/>.
/// </summary>
public sealed record CreateCodexPlanningAttemptCommand(Guid RunId, Guid? RepairSourceAttemptId = null)
    : IManualTransactionCommand<Result<CreateCodexPlanningAttemptCommandResult>>;
