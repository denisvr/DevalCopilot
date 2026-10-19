using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.AdvanceAgentSupervisionPhase;

/// <summary>
/// Moves the observation of the original observer of the original live host to a strictly later phase (ADR-0056), fixing the new
/// absolute deadline now (the attempt's actual reserved invocation timeout plus two minutes for AwaitingAdapterResult). Before the move,
/// the previous phase's deadline and the heartbeat freshness are judged under the write lock and any first attention is recorded, so a
/// late writer cannot erase a missed interval. A phase never repeats, regresses or extends a deadline. Manual transaction.
/// </summary>
public sealed record AdvanceAgentSupervisionPhaseCommand(
    Guid AttemptId, Guid HostInstanceId, Guid ObserverId, AgentSupervisionPhase Phase)
    : IManualTransactionCommand<Result<AdvanceAgentSupervisionPhaseCommandResult>>;
