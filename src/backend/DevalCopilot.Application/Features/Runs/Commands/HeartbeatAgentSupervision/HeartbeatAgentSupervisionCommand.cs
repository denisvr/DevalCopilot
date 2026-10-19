using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.HeartbeatAgentSupervision;

/// <summary>
/// One committed heartbeat of the original observer of the original live host (ADR-0056). Before it is accepted, the stored phase
/// deadline and the stored heartbeat freshness are judged under the write lock and any first attention is recorded; the heartbeat then
/// moves neither the phase nor its deadline and never clears an attention. It appends no event of its own. Manual transaction.
/// </summary>
public sealed record HeartbeatAgentSupervisionCommand(Guid AttemptId, Guid HostInstanceId, Guid ObserverId)
    : IManualTransactionCommand<Result<HeartbeatAgentSupervisionCommandResult>>;
