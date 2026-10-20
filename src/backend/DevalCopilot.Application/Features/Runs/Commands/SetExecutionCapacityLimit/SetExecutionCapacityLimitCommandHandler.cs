using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit;

/// <summary>
/// Changes the recorded-mutating admission limit in one short write-locked transaction (ADR-0059). The persisted limit and the coherent
/// ownership are read after the transaction began, so no claim can commit between the decision and the appended revision, and a claim that
/// begins later reads the new limit. Unavailable configuration or ownership, a limit below the recorded owners, and revision overflow
/// refuse with nothing written; the same value is an idempotent no-op that repairs and releases nothing. Lowering never strands an
/// admitted owner, picks a winner or saves a pending request.
/// </summary>
public sealed class SetExecutionCapacityLimitCommandHandler(IDevalCopilotDbContext dbContext)
    : ICommandHandler<SetExecutionCapacityLimitCommand, Result<SetExecutionCapacityLimitCommandResult>>
{
    public async Task<Result<SetExecutionCapacityLimitCommandResult>> HandleAsync(
        SetExecutionCapacityLimitCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var reading = await ExecutionCapacityPolicy.ReadAsync(dbContext, null, cancellationToken);
        if (reading is not { Limit: { } current, RecordedOwners: { } owners, Revision: { } revision })
        {
            return Failure(ExecutionCapacityErrors.Unavailable());
        }

        if (command.Limit == current)
        {
            return Success(reading);
        }

        if (owners > command.Limit)
        {
            return Failure(ExecutionCapacityErrors.LimitBelowRecordedOwners());
        }

        if (ExecutionCapacityConfigurationRevision.NextRevisionAfter(revision) is not { } next)
        {
            return Failure(ExecutionCapacityErrors.Unavailable());
        }

        dbContext.ExecutionCapacityConfigurations.Add(ExecutionCapacityConfigurationRevision.Record(next, command.Limit));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Success(new ExecutionCapacityReading(
            owners >= command.Limit ? ExecutionCapacityStatus.AtCapacity : ExecutionCapacityStatus.Available,
            owners,
            command.Limit,
            next));
    }

    private static Result<SetExecutionCapacityLimitCommandResult> Failure(Error error) =>
        Result<SetExecutionCapacityLimitCommandResult>.Failure(error);

    private static Result<SetExecutionCapacityLimitCommandResult> Success(ExecutionCapacityReading reading) =>
        Result<SetExecutionCapacityLimitCommandResult>.Success(new SetExecutionCapacityLimitCommandResult(
            reading.Status, reading.Limit, reading.RecordedOwners, ExecutionCapacityPolicy.ReasonCodeOf(reading.Status)));
}
