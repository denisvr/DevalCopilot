using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ObserveAgentSupervision;

/// <summary>
/// One independent sweep observation of one open Agent supervision (ADR-0056), decided from fresh durable state inside one short write
/// transaction and with no external work, so it still observes while the role supervisor, a Git preparation, an adapter or the result
/// recorder is blocked. It records the first attention once (a stale heartbeat or an elapsed phase deadline, the earlier boundary), closes
/// the observation of a host that is no longer running, and closes the observation of an Attempt that is already terminal without
/// turning a timely result into a late failure. It never claims, retries, cancels or terminalizes anything. Manual transaction.
/// </summary>
public sealed record ObserveAgentSupervisionCommand(Guid SupervisionId)
    : IManualTransactionCommand<Result<ObserveAgentSupervisionCommandResult>>;
