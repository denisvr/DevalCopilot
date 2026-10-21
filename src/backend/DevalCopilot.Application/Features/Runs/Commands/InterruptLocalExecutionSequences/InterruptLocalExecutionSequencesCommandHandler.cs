using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.InterruptLocalExecutionSequences;

public sealed class InterruptLocalExecutionSequencesCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<InterruptLocalExecutionSequencesCommand, Result<InterruptLocalExecutionSequencesCommandResult>>
{
    public async Task<Result<InterruptLocalExecutionSequencesCommandResult>> HandleAsync(
        InterruptLocalExecutionSequencesCommand command, CancellationToken cancellationToken)
    {
        // Every sequence that could still advance or drain, in a fixed order. A stored state this build does not recognize is never
        // materialized; the database check constraint makes that unreachable.
        var liveRows = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.State == LocalExecutionSequenceState.Armed
                || candidate.State == LocalExecutionSequenceState.PreparingStep
                || candidate.State == LocalExecutionSequenceState.AwaitingStepResult
                || candidate.State == LocalExecutionSequenceState.Stopping)
            .Select(candidate => new { candidate.Id, candidate.ArmedAtUtc })
            .ToListAsync(cancellationToken);

        // SQLite cannot order a DateTimeOffset, so the live sequences (a handful) are ordered here.
        var interrupted = 0;
        foreach (var sequenceId in liveRows.OrderBy(candidate => candidate.ArmedAtUtc).Select(candidate => candidate.Id))
        {
            if (await InterruptOneAsync(sequenceId, cancellationToken))
            {
                interrupted++;
            }
        }

        var refused = await RefuseUndispatchedAgentClaimsAsync(cancellationToken) + await RefuseUndispatchedVerificationClaimsAsync(cancellationToken);
        return Result<InterruptLocalExecutionSequencesCommandResult>.Success(
            new InterruptLocalExecutionSequencesCommandResult(interrupted, refused));
    }

    private async Task<bool> InterruptOneAsync(Guid sequenceId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        var runId = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.Id == sequenceId)
            .Select(candidate => (Guid?)candidate.RunId)
            .SingleOrDefaultAsync(cancellationToken);
        if (runId is not { } run)
        {
            return false;
        }

        // The run row, never the sequence row: an ended sequence is immutable by database guard, and even a no-op write to it is refused.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == run)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return false;
        }

        var sequence = await dbContext.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == sequenceId, cancellationToken);
        await dbContext.Entry(sequence).ReloadAsync(cancellationToken);
        if (!sequence.IsAuthorityLive)
        {
            return false;
        }

        var nowUtc = timeProvider.GetUtcNow();
        var reserved = await dbContext.LocalExecutionSequenceSteps
            .Where(candidate => candidate.SequenceId == sequenceId && candidate.State == LocalExecutionSequenceStepState.Reserved)
            .ToListAsync(cancellationToken);
        foreach (var step in reserved)
        {
            await dbContext.Entry(step).ReloadAsync(cancellationToken);
            var operationExists = step.IsAgent
                ? await dbContext.Attempts.AsNoTracking().AnyAsync(candidate => candidate.Id == step.ReservedOperationId, cancellationToken)
                : await dbContext.VerificationExecutions.AsNoTracking()
                    .AnyAsync(candidate => candidate.Id == step.ReservedOperationId, cancellationToken);
            if (operationExists)
            {
                step.MarkUnresolved(LocalExecutionSequenceReason.HostRestarted, nowUtc);
            }
            else
            {
                step.Revoke(LocalExecutionSequenceReason.HostRestarted, nowUtc);
            }
        }

        sequence.End(LocalExecutionSequenceState.Interrupted, LocalExecutionSequenceReason.HostRestarted, detailCode: null, nowUtc);
        dbContext.Events.Add(LocalExecutionSequenceEvents.Ended(sequence, nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The next iteration must not reuse tracked state of this sequence.
        dbContext.Entry(sequence).State = EntityState.Detached;
        foreach (var step in reserved)
        {
            dbContext.Entry(step).State = EntityState.Detached;
        }

        return true;
    }

    /// <summary>A linked Agent claim of an ended sequence that was proved never dispatched (a Running attempt with no dispatch marker)
    /// is closed with the dedicated refusal and without invoking its adapter. A dispatched attempt is never touched here.</summary>
    private async Task<int> RefuseUndispatchedAgentClaimsAsync(CancellationToken cancellationToken)
    {
        var candidates = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(step => step.Kind != LocalExecutionStepKind.Verification
                && step.OperationId != null
                && dbContext.LocalExecutionSequences.Any(sequence => sequence.Id == step.SequenceId
                    && sequence.State != LocalExecutionSequenceState.Armed
                    && sequence.State != LocalExecutionSequenceState.PreparingStep
                    && sequence.State != LocalExecutionSequenceState.AwaitingStepResult
                    && sequence.State != LocalExecutionSequenceState.Stopping)
                && dbContext.Attempts.Any(attempt => attempt.Id == step.OperationId
                    && attempt.Status == AttemptStatus.Running
                    && attempt.AgentDispatchedAtUtc == null))
            .Select(step => new { step.OperationId, step.SequenceId })
            .ToListAsync(cancellationToken);

        var refused = 0;
        foreach (var candidate in candidates)
        {
            await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
            var attemptId = candidate.OperationId!.Value;
            var runId = await dbContext.Attempts.AsNoTracking()
                .Where(attempt => attempt.Id == attemptId)
                .Select(attempt => (Guid?)attempt.RunId)
                .SingleOrDefaultAsync(cancellationToken);
            if (runId is not { } run)
            {
                continue;
            }

            var locked = await dbContext.Runs
                .Where(candidateRun => candidateRun.Id == run)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Lifecycle, row => row.Lifecycle), cancellationToken);
            var attempt = locked == 1
                ? await dbContext.Attempts.SingleOrDefaultAsync(row => row.Id == attemptId, cancellationToken)
                : null;
            if (attempt is null || attempt.Status != AttemptStatus.Running || attempt.AgentDispatchedAtUtc.HasValue)
            {
                continue;
            }

            // Full sequence, run, project, workspace, source, kind and contract identity plus the exact reserved-and-claimed link; anything
            // less stays Running and is never terminalized, so its ownership and quarantine are untouched.
            if (!await LocalExecutionRefusalProof.ProvesUndispatchedAgentClaimAsync(dbContext, attempt, cancellationToken))
            {
                continue;
            }

            var nowUtc = timeProvider.GetUtcNow();
            attempt.CompleteLocalExecutionSequenceDispatchRefused(nowUtc);
            dbContext.Events.Add(RunEvent.Record(
                Guid.NewGuid(),
                run,
                attemptId,
                RunEventType.AgentAttemptCompleted,
                ParticipantIdentity.ForOrchestrator(),
                JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome.ToString() }),
                nowUtc));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            dbContext.Entry(attempt).State = EntityState.Detached;
            refused++;
        }

        return refused;
    }

    private async Task<int> RefuseUndispatchedVerificationClaimsAsync(CancellationToken cancellationToken)
    {
        var candidates = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(step => step.Kind == LocalExecutionStepKind.Verification
                && step.OperationId != null
                && dbContext.LocalExecutionSequences.Any(sequence => sequence.Id == step.SequenceId
                    && sequence.State != LocalExecutionSequenceState.Armed
                    && sequence.State != LocalExecutionSequenceState.PreparingStep
                    && sequence.State != LocalExecutionSequenceState.AwaitingStepResult
                    && sequence.State != LocalExecutionSequenceState.Stopping)
                && dbContext.VerificationExecutions.Any(execution => execution.Id == step.OperationId
                    && execution.Status == VerificationExecutionStatus.Running
                    && execution.DispatchedAtUtc == null))
            .Select(step => step.OperationId!.Value)
            .ToListAsync(cancellationToken);

        var refused = 0;
        foreach (var executionId in candidates)
        {
            var execution = await dbContext.VerificationExecutions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == executionId, cancellationToken);
            if (execution is null || !await LocalExecutionRefusalProof.ProvesUndispatchedVerificationClaimAsync(dbContext, execution, cancellationToken))
            {
                continue;
            }

            var nowUtc = timeProvider.GetUtcNow();
            var updated = await dbContext.VerificationExecutions
                .Where(execution => execution.Id == executionId
                    && execution.Status == VerificationExecutionStatus.Running
                    && execution.DispatchedAtUtc == null
                    && dbContext.LocalExecutionSequenceSteps.Any(step => step.OperationId == execution.Id
                        && step.ReservedOperationId == execution.Id
                        && step.Kind == LocalExecutionStepKind.Verification))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(execution => execution.Status, VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused)
                        .SetProperty(execution => execution.CompletedAtUtc, nowUtc),
                    cancellationToken);
            refused += updated;
        }

        return refused;
    }
}
