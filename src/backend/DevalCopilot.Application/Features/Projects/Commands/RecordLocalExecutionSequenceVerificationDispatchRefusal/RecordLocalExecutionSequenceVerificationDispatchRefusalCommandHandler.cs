using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordLocalExecutionSequenceVerificationDispatchRefusal;

public sealed class RecordLocalExecutionSequenceVerificationDispatchRefusalCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordLocalExecutionSequenceVerificationDispatchRefusalCommand, Result>
{
    private const string NotEligibleCode = "verification.execution_not_dispatchable";

    public async Task<Result> HandleAsync(
        RecordLocalExecutionSequenceVerificationDispatchRefusalCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The transaction's first statement: a no-op write that takes the database write lock. The proof below is read after it, so a
        // sequence transition that committed first is seen here and the recorded status is atomic with the proof.
        var locked = await dbContext.VerificationExecutions
            .Where(candidate => candidate.Id == command.ExecutionId && candidate.ProjectId == command.ProjectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, candidate => candidate.Status), cancellationToken);
        var execution = locked == 1
            ? await dbContext.VerificationExecutions.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == command.ExecutionId, cancellationToken)
            : null;
        if (execution is null)
        {
            return Result.Failure(Error.NotFound("verification.execution_not_found", "This verification execution was not found."));
        }

        if (execution.Status == VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused && !execution.DispatchedAtUtc.HasValue)
        {
            return Result.Success();
        }

        if (execution.Status != VerificationExecutionStatus.Running || execution.DispatchedAtUtc.HasValue)
        {
            return Result.Failure(Error.Conflict(
                NotEligibleCode, "Only an undispatched, running verification execution can record a local execution sequence refusal."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        var evaluation = await LocalExecutionDispatchGate.EvaluateVerificationAsync(dbContext, execution, nowUtc, cancellationToken);
        if (evaluation.Verdict != LocalExecutionDispatchGate.Verdict.Refused || !await IsProvedLinkAsync(execution, evaluation, cancellationToken))
        {
            // Unlinked, still dispatchable, or an association that cannot be proved: no status is fabricated.
            return Result.Failure(Error.Conflict(
                NotEligibleCode, "No provable local execution sequence refusal of this verification execution exists."));
        }

        execution.MarkLocalExecutionSequenceDispatchRefused(nowUtc);
        var updated = await dbContext.VerificationExecutions
            .Where(candidate => candidate.Id == command.ExecutionId
                && candidate.Status == VerificationExecutionStatus.Running
                && candidate.DispatchedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Status, VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused)
                    .SetProperty(candidate => candidate.CompletedAtUtc, nowUtc),
                cancellationToken);
        if (updated != 1)
        {
            return Result.Failure(Error.Conflict(NotEligibleCode, "This verification execution can no longer record a refusal."));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>The refusal is recorded only for a coherent association: the step names this exact execution as both its reserved and its
    /// claimed operation, for the same project, in a Claimed or Concluded verification step of the evaluated sequence.</summary>
    private async Task<bool> IsProvedLinkAsync(
        VerificationExecution execution, LocalExecutionDispatchGate.Evaluation evaluation, CancellationToken cancellationToken)
    {
        if (evaluation.Sequence is not { } sequence || sequence.ProjectId != execution.ProjectId
            || !await LocalExecutionRefusalProof.ProvesUndispatchedVerificationClaimAsync(dbContext, execution, cancellationToken))
        {
            return false;
        }

        return await dbContext.LocalExecutionSequenceSteps.AsNoTracking().AnyAsync(
            step => step.SequenceId == sequence.Id
                && step.Kind == LocalExecutionStepKind.Verification
                && step.ReservedOperationId == execution.Id
                && step.OperationId == execution.Id
                && (step.State == LocalExecutionSequenceStepState.Claimed || step.State == LocalExecutionSequenceStepState.Concluded),
            cancellationToken);
    }
}
