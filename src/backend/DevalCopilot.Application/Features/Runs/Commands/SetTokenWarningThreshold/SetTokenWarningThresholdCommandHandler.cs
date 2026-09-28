using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenWarningThreshold;

/// <summary>
/// Durably records one provider's advisory token-warning threshold and its change event in one
/// <c>SaveChangesAsync</c> call. No provider is contacted and no evidence is rewritten: the cockpit
/// re-derives the warning from already-recorded evidence on its next read.
///
/// <para>
/// <c>Run.Lifecycle</c> is an EF concurrency token (see <c>RunConfiguration</c>). A lifecycle
/// transition committed between this handler's read and its save makes the UPDATE match zero rows:
/// the failed save rolls back the Run change and the queued event together (this is a
/// manual-transaction command, so the save owns its own transaction), and the handler reports a
/// terminal run as not editable and any other cause as a retryable conflict. The threshold columns
/// are deliberately not concurrency tokens: a threshold write must never make a claim's own Run
/// UPDATE fail, and SQLite serializes write transactions, so two concurrent threshold writes each
/// commit their own value and event atomically and the last committed value and event agree. EF
/// updates only the modified column, so setting one provider never overwrites the other.
/// </para>
/// </summary>
public sealed class SetTokenWarningThresholdCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<SetTokenWarningThresholdCommand, Result<SetTokenWarningThresholdCommandResult>>
{
    public async Task<Result<SetTokenWarningThresholdCommandResult>> HandleAsync(
        SetTokenWarningThresholdCommand command, CancellationToken cancellationToken)
    {
        // The validator already rejects an unknown provider; this is the handler's own fail-closed guard.
        if (!TokenWarningProviders.TryParse(command.Provider, out var provider))
        {
            return Result<SetTokenWarningThresholdCommandResult>.Failure(
                Error.Failure("token_warning.invalid", "The provider must be Codex or ClaudeCode."));
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<SetTokenWarningThresholdCommandResult>.Failure(TokenWarningThresholdErrors.RunNotFound());
        }

        try
        {
            run.SetTokenWarningThreshold(provider, command.ThresholdTokens);
        }
        catch (InvalidOperationException)
        {
            return Result<SetTokenWarningThresholdCommandResult>.Failure(TokenWarningThresholdErrors.RunNotEditable());
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result<SetTokenWarningThresholdCommandResult>.Failure(
                Error.Failure("token_warning.invalid", "The threshold must be positive and within the supported maximum."));
        }

        // Force the Run UPDATE even when the value is unchanged, so its Lifecycle concurrency-token
        // WHERE clause always guards the lifecycle: a no-op set must not append an event to a Run
        // that became terminal after the read above.
        var entry = dbContext.Entry(run);
        if (provider == AgentProvider.Codex)
        {
            entry.Property(candidate => candidate.CodexTokenWarningThreshold).IsModified = true;
        }
        else
        {
            entry.Property(candidate => candidate.ClaudeTokenWarningThreshold).IsModified = true;
        }

        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            attemptId: null,
            RunEventType.TokenWarningThresholdChanged,
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
                null => Result<SetTokenWarningThresholdCommandResult>.Failure(TokenWarningThresholdErrors.RunNotFound()),
                RunLifecycle.Created or RunLifecycle.Running =>
                    Result<SetTokenWarningThresholdCommandResult>.Failure(TokenWarningThresholdErrors.ConcurrentChange()),
                _ => Result<SetTokenWarningThresholdCommandResult>.Failure(TokenWarningThresholdErrors.RunNotEditable()),
            };
        }

        return Result<SetTokenWarningThresholdCommandResult>.Success(
            new SetTokenWarningThresholdCommandResult(provider.ToString(), command.ThresholdTokens));
    }
}
