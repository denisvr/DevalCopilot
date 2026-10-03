using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

public sealed class MarkVerificationExecutionDispatchedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<MarkVerificationExecutionDispatchedCommand, Result>
{
    /// <summary>One short write-locked transaction: the first statement is a self-referential no-op write that takes the lock,
    /// the execution and its ownership are then re-read untracked and must agree with the expected snapshot, and only then is the
    /// single-use marker committed conditionally. Nothing external happens under the lock; a refusal leaves the execution pending.</summary>
    public async Task<Result> HandleAsync(MarkVerificationExecutionDispatchedCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var locked = await dbContext.VerificationExecutions
            .Where(candidate => candidate.Id == command.VerificationExecutionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, candidate => candidate.Status), cancellationToken);
        var execution = locked == 1
            ? await VerificationDispatchAuthority.ReadExecutionAsync(dbContext, command.VerificationExecutionId, cancellationToken)
            : null;
        if (execution is null)
        {
            return Result.Failure(Error.NotFound("verification.execution_not_found", "This verification execution was not found."));
        }

        if (execution.Status != VerificationExecutionStatus.Running)
        {
            return Result.Failure(Error.Conflict("verification.execution_not_dispatchable", "This verification execution cannot be dispatched."));
        }

        if (execution.DispatchedAtUtc.HasValue)
        {
            return Result.Failure(Error.Conflict("verification.execution_already_dispatched", "This verification execution was already dispatched."));
        }

        if (await VerificationDispatchAuthority.ConfirmAsync(dbContext, execution, command.Expected, cancellationToken) is { } refusal)
        {
            return Result.Failure(refusal);
        }

        var dispatchedAtUtc = timeProvider.GetUtcNow();
        var updated = await dbContext.VerificationExecutions
            .Where(candidate => candidate.Id == command.VerificationExecutionId
                && candidate.Status == VerificationExecutionStatus.Running
                && candidate.DispatchedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.DispatchedAtUtc, dispatchedAtUtc), cancellationToken);
        if (updated != 1)
        {
            return Result.Failure(Error.Conflict("verification.execution_already_dispatched", "This verification execution was already dispatched."));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
