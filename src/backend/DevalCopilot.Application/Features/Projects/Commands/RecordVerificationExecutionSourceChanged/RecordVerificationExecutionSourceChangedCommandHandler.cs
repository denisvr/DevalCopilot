using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionSourceChanged;

public sealed class RecordVerificationExecutionSourceChangedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordVerificationExecutionSourceChangedCommand, Result>
{
    /// <summary>The same short write-locked boundary as the dispatch marker: the observation is recorded as a pre-dispatch
    /// SourceChanged only while the durable execution is still pending, agrees with the snapshot it was taken against and is still
    /// owned by the project's current ready workspace. Ownership loss or disagreement records nothing: no fingerprint and no
    /// process outcome is ever fabricated for an execution the observation did not describe.</summary>
    public async Task<Result> HandleAsync(RecordVerificationExecutionSourceChangedCommand command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CompletionFingerprintSha256);

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

        if (execution.Status != VerificationExecutionStatus.Running || execution.DispatchedAtUtc.HasValue)
        {
            return NotPending();
        }

        if (await VerificationDispatchAuthority.ConfirmAsync(dbContext, execution, command.Expected, cancellationToken) is { } refusal)
        {
            return Result.Failure(refusal);
        }

        var completedAtUtc = timeProvider.GetUtcNow();
        var updated = await dbContext.VerificationExecutions
            .Where(candidate => candidate.Id == command.VerificationExecutionId
                && candidate.Status == VerificationExecutionStatus.Running
                && candidate.DispatchedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.CompletionFingerprintSha256, command.CompletionFingerprintSha256)
                    .SetProperty(candidate => candidate.Status, VerificationExecutionStatus.SourceChanged)
                    .SetProperty(candidate => candidate.CompletedAtUtc, completedAtUtc),
                cancellationToken);
        if (updated != 1)
        {
            return NotPending();
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private static Result NotPending() => Result.Failure(Error.Conflict(
        "verification.execution_not_pending",
        "This verification execution is no longer pending."));
}
