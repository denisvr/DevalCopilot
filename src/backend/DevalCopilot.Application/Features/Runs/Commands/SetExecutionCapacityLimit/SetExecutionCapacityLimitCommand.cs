using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit;

/// <summary>
/// The one explicit operator choice of the host-wide recorded-mutating admission limit, one or two (ADR-0059). It reads the persisted
/// limit and the coherent recorded ownership under the database write boundary and either returns the current reading unchanged (the
/// same value), refuses a limit below the recorded owners, refuses an unreadable state, or appends exactly one next configuration revision.
/// It starts, cancels, releases, retries and queues nothing and grants no execution.
/// </summary>
public sealed record SetExecutionCapacityLimitCommand(int Limit)
    : IManualTransactionCommand<Result<SetExecutionCapacityLimitCommandResult>>;
