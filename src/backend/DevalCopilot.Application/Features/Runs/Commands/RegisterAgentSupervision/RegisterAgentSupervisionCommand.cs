using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RegisterAgentSupervision;

/// <summary>
/// Registers the one host observation of one already claimed, still Running Agent attempt (ADR-0056), at entry to its supervisor's
/// per-attempt execution. An identical registration replays; a competing identity never replaces or adopts an existing one. It claims,
/// dispatches, retries and terminalizes nothing. Manual transaction.
/// </summary>
public sealed record RegisterAgentSupervisionCommand(Guid RunId, Guid AttemptId, Guid HostInstanceId, Guid ObserverId)
    : IManualTransactionCommand<Result<RegisterAgentSupervisionCommandResult>>;
