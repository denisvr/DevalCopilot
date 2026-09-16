using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionSourceChanged;

public sealed class RecordVerificationExecutionSourceChangedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordVerificationExecutionSourceChangedCommand, Result>
{
    public async Task<Result> HandleAsync(RecordVerificationExecutionSourceChangedCommand command, CancellationToken cancellationToken)
    {
        var execution = await dbContext.VerificationExecutions.SingleOrDefaultAsync(
            candidate => candidate.Id == command.VerificationExecutionId, cancellationToken);
        if (execution is null)
        {
            return Result.Failure(Error.NotFound("verification.execution_not_found", "This verification execution was not found."));
        }

        try
        {
            execution.MarkSourceChangedBeforeDispatch(command.CompletionFingerprintSha256, timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(Error.Conflict(
                "verification.execution_not_pending",
                "This verification execution is no longer pending."));
        }

        return Result.Success();
    }
}
