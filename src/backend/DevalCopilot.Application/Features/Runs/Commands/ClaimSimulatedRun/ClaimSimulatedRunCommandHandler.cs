using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ClaimSimulatedRun;

public sealed class ClaimSimulatedRunCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<ClaimSimulatedRunCommand, Result<ClaimSimulatedRunCommandResult>>
{
    public async Task<Result<ClaimSimulatedRunCommandResult>> HandleAsync(
        ClaimSimulatedRunCommand command,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs
            .SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);

        if (run is null)
        {
            return Result<ClaimSimulatedRunCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        // Read afresh (never from the tracked Run) and guarded by the mode's concurrency token, so the save
        // below commits only while the stored mode is still the one this claim decided against.
        var executionModeError = await CurrentRunExecutionMode.ReadAndGuardSimulationAsync(dbContext, run, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<ClaimSimulatedRunCommandResult>.Failure(executionModeError);
        }

        if (run.Lifecycle != RunLifecycle.Created)
        {
            return Result<ClaimSimulatedRunCommandResult>.Failure(
                Error.Conflict("runs.already_claimed", "The run has already been claimed."));
        }

        var attemptNumber = await dbContext.Attempts
            .Where(candidate => candidate.RunId == run.Id)
            .CountAsync(cancellationToken) + 1;

        var nowUtc = timeProvider.GetUtcNow();
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, attemptNumber, nowUtc);
        dbContext.Attempts.Add(attempt);
        run.Claim(nowUtc);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Attempts.Remove(attempt);
            return Result<ClaimSimulatedRunCommandResult>.Failure(
                await CurrentRunExecutionMode.HasChangedAsync(dbContext, run, cancellationToken)
                    ? CurrentRunExecutionMode.ChangedDuringClaim()
                    : Error.Conflict("runs.already_claimed", "The run has already been claimed."));
        }

        return Result<ClaimSimulatedRunCommandResult>.Success(
            new ClaimSimulatedRunCommandResult(attempt.Id, attempt.AttemptNumber));
    }
}
