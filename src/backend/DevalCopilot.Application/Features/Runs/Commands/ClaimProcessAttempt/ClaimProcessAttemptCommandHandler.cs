using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ClaimProcessAttempt;

public sealed class ClaimProcessAttemptCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<ClaimProcessAttemptCommand, Result<ClaimProcessAttemptCommandResult>>
{
    public async Task<Result<ClaimProcessAttemptCommandResult>> HandleAsync(
        ClaimProcessAttemptCommand command,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs
            .SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);

        if (run is null)
        {
            return Result<ClaimProcessAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        // Standalone Process support is historical: only a Legacy run admits it. Read afresh and guarded by the
        // mode's concurrency token, so the save below commits only while the stored mode is unchanged.
        var executionModeError = await CurrentRunExecutionMode.ReadAndGuardProcessAsync(dbContext, run, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<ClaimProcessAttemptCommandResult>.Failure(executionModeError);
        }

        if (run.Lifecycle != RunLifecycle.Created)
        {
            return Result<ClaimProcessAttemptCommandResult>.Failure(
                Error.Conflict("runs.already_claimed", "The run has already been claimed."));
        }

        var attemptNumber = await dbContext.Attempts
            .Where(candidate => candidate.RunId == run.Id)
            .CountAsync(cancellationToken) + 1;

        var nowUtc = timeProvider.GetUtcNow();
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, attemptNumber, command.Intent, nowUtc);
        dbContext.Attempts.Add(attempt);
        run.Claim(nowUtc);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Attempts.Remove(attempt);
            return Result<ClaimProcessAttemptCommandResult>.Failure(
                await CurrentRunExecutionMode.HasChangedAsync(dbContext, run, cancellationToken)
                    ? CurrentRunExecutionMode.ChangedDuringClaim()
                    : Error.Conflict("runs.already_claimed", "The run has already been claimed."));
        }

        return Result<ClaimProcessAttemptCommandResult>.Success(
            new ClaimProcessAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
    }
}
