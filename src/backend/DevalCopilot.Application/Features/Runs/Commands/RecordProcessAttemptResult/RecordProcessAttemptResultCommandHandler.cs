using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordProcessAttemptResult;

public sealed class RecordProcessAttemptResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>
{
    public async Task<Result<AttemptStatus>> HandleAsync(
        RecordProcessAttemptResultCommand command,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs
            .SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (run is null || attempt is null)
        {
            return Result<AttemptStatus>.Failure(Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.RunId != run.Id)
        {
            return Result<AttemptStatus>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Process)
        {
            return Result<AttemptStatus>.Failure(
                Error.Conflict("attempts.not_process", "The attempt is not a Process attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || run.Lifecycle != RunLifecycle.Running)
        {
            return Result<AttemptStatus>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a result."));
        }

        var nowUtc = timeProvider.GetUtcNow();

        if (command.Outcome is { } outcome)
        {
            attempt.CompleteProcess(outcome, command.ExitCode, nowUtc);
        }
        else
        {
            attempt.Fail(nowUtc);
        }

        if (attempt.Status == AttemptStatus.Completed)
        {
            run.Complete(nowUtc);
        }
        else
        {
            run.Fail(nowUtc);
        }

        return Result<AttemptStatus>.Success(attempt.Status);
    }
}
