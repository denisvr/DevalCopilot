using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ReserveLocalExecutionAdmission;

/// <summary>
/// Reserves the oldest eligible pending admission of a queued cycle (ADR-0062), for a host worker that already acquired its in-process
/// preparation capacity: never a durable reservation held merely while waiting for that semaphore. One short write-locked command
/// chooses the admission by its database order, revalidates its whole database authority and the current workspace and capacity
/// ownership and the bound of four held preparations, creates its exact Reserved step and original-host preparation token, and marks the
/// admission Reserved atomically. The result carries the internal ticket only for the call that reserved. No external preparation, claim
/// or retry runs here.
/// </summary>
public sealed record ReserveLocalExecutionAdmissionCommand : IManualTransactionCommand<Result<ReserveLocalExecutionAdmissionCommandResult>>;
