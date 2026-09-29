using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;

/// <summary>
/// Durably records one provider's token stop threshold and its change event in one
/// <c>SaveChangesAsync</c> call. No provider is contacted and no evidence is rewritten: the next
/// claim (and the next cockpit read) re-derives the stop from already-recorded evidence.
///
/// <para>
/// <c>Run.Lifecycle</c> and both stop-threshold columns are EF concurrency tokens (see
/// <c>RunConfiguration</c>). A committed change to the value of any configured concurrency token
/// between this handler's read and its save, that is, a lifecycle transition, a Claude model or
/// effort request change, or a stop-threshold write to either provider, makes the
/// UPDATE match zero rows: the failed save rolls back the Run change and the queued event together
/// (a manual-transaction command, so the save owns its own transaction), and the handler reports a
/// terminal run as not editable and any other cause as a retryable conflict. The same tokens are
/// what stop an Agent claim from committing against a policy this write has since changed. EF
/// updates only the modified column, so setting one provider never overwrites the other.
/// </para>
/// </summary>
public sealed class SetTokenStopThresholdCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<SetTokenStopThresholdCommand, Result<SetTokenStopThresholdCommandResult>>
{
    public async Task<Result<SetTokenStopThresholdCommandResult>> HandleAsync(
        SetTokenStopThresholdCommand command, CancellationToken cancellationToken)
    {
        // The validator already rejects an unknown provider; this is the handler's own fail-closed guard.
        if (!TokenStopProviders.TryParse(command.Provider, out var provider))
        {
            return Result<SetTokenStopThresholdCommandResult>.Failure(
                Error.Failure(TokenStopThresholdErrors.InvalidCode, "The provider must be Codex or ClaudeCode."));
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<SetTokenStopThresholdCommandResult>.Failure(TokenStopThresholdErrors.RunNotFound());
        }

        try
        {
            run.SetTokenStopThreshold(provider, command.ThresholdTokens);
        }
        catch (InvalidOperationException)
        {
            return Result<SetTokenStopThresholdCommandResult>.Failure(TokenStopThresholdErrors.RunNotEditable());
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result<SetTokenStopThresholdCommandResult>.Failure(
                Error.Failure(TokenStopThresholdErrors.InvalidCode, "The threshold must be positive and within the supported maximum."));
        }

        // Force the Run UPDATE even when the value is unchanged, so its concurrency-token WHERE
        // clause always guards the lifecycle and the policy: a no-op set must not append an event
        // to a Run that became terminal after the read above.
        var entry = dbContext.Entry(run);
        if (provider == AgentProvider.Codex)
        {
            entry.Property(candidate => candidate.CodexTokenStopThreshold).IsModified = true;
        }
        else
        {
            entry.Property(candidate => candidate.ClaudeTokenStopThreshold).IsModified = true;
        }

        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            attemptId: null,
            RunEventType.TokenStopThresholdChanged,
            ParticipantIdentity.ForHuman(),
            JsonSerializer.Serialize(new { provider = provider.ToString(), thresholdTokens = command.ThresholdTokens }),
            timeProvider.GetUtcNow()));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var lifecycle = await dbContext.Runs
                .AsNoTracking()
                .Where(candidate => candidate.Id == command.RunId)
                .Select(candidate => (RunLifecycle?)candidate.Lifecycle)
                .SingleOrDefaultAsync(cancellationToken);

            return lifecycle switch
            {
                null => Result<SetTokenStopThresholdCommandResult>.Failure(TokenStopThresholdErrors.RunNotFound()),
                RunLifecycle.Created or RunLifecycle.Running =>
                    Result<SetTokenStopThresholdCommandResult>.Failure(TokenStopThresholdErrors.ConcurrentChange()),
                _ => Result<SetTokenStopThresholdCommandResult>.Failure(TokenStopThresholdErrors.RunNotEditable()),
            };
        }

        return Result<SetTokenStopThresholdCommandResult>.Success(
            new SetTokenStopThresholdCommandResult(provider.ToString(), command.ThresholdTokens));
    }
}
