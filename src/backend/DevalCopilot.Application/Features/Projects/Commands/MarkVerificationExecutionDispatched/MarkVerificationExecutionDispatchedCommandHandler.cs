using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

public sealed class MarkVerificationExecutionDispatchedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<MarkVerificationExecutionDispatchedCommand, Result>
{
    public async Task<Result> HandleAsync(MarkVerificationExecutionDispatchedCommand command, CancellationToken cancellationToken)
    {
        var execution = await dbContext.VerificationExecutions.SingleOrDefaultAsync(
            candidate => candidate.Id == command.VerificationExecutionId, cancellationToken);
        if (execution is null)
        {
            return Result.Failure(Error.NotFound("verification.execution_not_found", "This verification execution was not found."));
        }

        if (execution.Status != VerificationExecutionStatus.Running || execution.DispatchedAtUtc.HasValue)
        {
            return Result.Failure(Error.Conflict("verification.execution_not_dispatchable", "This verification execution cannot be dispatched."));
        }

        var dispatchedAtUtc = timeProvider.GetUtcNow();
        var updated = await dbContext.VerificationExecutions
            .Where(candidate => candidate.Id == command.VerificationExecutionId
                && candidate.Status == VerificationExecutionStatus.Running
                && candidate.DispatchedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.DispatchedAtUtc, dispatchedAtUtc), cancellationToken);

        return updated == 1
            ? Result.Success()
            : Result.Failure(Error.Conflict("verification.execution_already_dispatched", "This verification execution was already dispatched."));
    }
}
