using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionQueue;

/// <summary>
/// One bounded keyset page of the coordinator's observation of waiting queued cycles (ADR-0062), decided entirely from fresh database
/// state inside one short write-locked transaction and with no external work. It visits at most 32 live queued sequences whose
/// coordination number is after <paramref name="AfterCoordinationNumber"/> and not past <paramref name="HighWaterCoordinationNumber"/>,
/// ends an elapsed or changed-authority cycle with a fixed reason, and records a changed fixed waiting reason (and its event) only when it
/// actually changed. A null high-water asks the command to fix it from the current maximum, so the host finishes a finite range per sweep
/// and refreshes it on wrap. It reserves, claims, retries or resumes nothing, and <paramref name="PreparationWorkerAvailable"/> is an
/// in-process observation that can only label a waiting reason.
/// </summary>
public sealed record ObserveLocalExecutionQueueCommand(
    long AfterCoordinationNumber, long? HighWaterCoordinationNumber, bool PreparationWorkerAvailable)
    : IManualTransactionCommand<Result<ObserveLocalExecutionQueueCommandResult>>;
