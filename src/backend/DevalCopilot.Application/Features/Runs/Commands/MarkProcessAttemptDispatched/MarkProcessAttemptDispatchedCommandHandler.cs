using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.MarkProcessAttemptDispatched;

public sealed class MarkProcessAttemptDispatchedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<MarkProcessAttemptDispatchedCommand, Result<DateTimeOffset>>
{
    public async Task<Result<DateTimeOffset>> HandleAsync(
        MarkProcessAttemptDispatchedCommand command,
        CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result<DateTimeOffset>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Process)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("attempts.not_process", "The attempt is not a Process attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot be dispatched."));
        }

        if (attempt.ProcessDispatchedAtUtc.HasValue)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("attempts.already_dispatched", "The attempt was already dispatched."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        attempt.MarkProcessDispatched(nowUtc);

        return Result<DateTimeOffset>.Success(nowUtc);
    }
}
