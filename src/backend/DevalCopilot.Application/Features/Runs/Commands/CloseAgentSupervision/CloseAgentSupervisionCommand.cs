using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CloseAgentSupervision;

/// <summary>
/// Closes the observation when its original observer ends (ADR-0056), from a fresh durable read of the Attempt inside the same write
/// transaction: a terminal Attempt closes it cleanly, a still Running Attempt records that the observer ended without a durable terminal
/// result. A successful method return in the supervisor is never evidence that a result committed. The first attention is retained. It
/// terminalizes, retries and cancels nothing. Manual transaction.
/// </summary>
public sealed record CloseAgentSupervisionCommand(Guid AttemptId, Guid HostInstanceId, Guid ObserverId)
    : IManualTransactionCommand<Result<CloseAgentSupervisionCommandResult>>;
