using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordCodexAccountUsageStop;

public sealed class RecordCodexAccountUsageStopCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordCodexAccountUsageStopCommand, Result<RecordCodexAccountUsageStopCommandResult>>
{
    public const string NotClaimedCode = "agent_attempts.account_usage_stop_not_claimed";
    public const string NotApplicableCode = "agent_attempts.account_usage_stop_not_applicable";
    public const string GuardMismatchCode = "agent_attempts.account_usage_guard_mismatch";

    /// <summary>One short write-locked transaction: the first statement is a self-referential no-op write that takes the lock, the
    /// attempt is then loaded and refreshed from the database (a tracked entity of this context may be stale, so nothing about it
    /// is trusted), every decision input is read from that fresh state, and the one mutation, the one save and the commit follow
    /// together. Nothing external happens under the lock. The decision is always for the attempt's actual stored snapshot: facts
    /// prepared for another threshold or launch tuple are unavailable evidence for it, and facts for another attempt are refused.</summary>
    public async Task<Result<RecordCodexAccountUsageStopCommandResult>> HandleAsync(RecordCodexAccountUsageStopCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var locked = await dbContext.Attempts
            .Where(candidate => candidate.Id == command.AttemptId && candidate.RunId == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, candidate => candidate.Status), cancellationToken);
        if (locked != 1)
        {
            return Result<RecordCodexAccountUsageStopCommandResult>.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        var attempt = await dbContext.Attempts.SingleAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);
        await dbContext.Entry(attempt).ReloadAsync(cancellationToken);

        if (attempt.Kind != AttemptKind.Agent || attempt.AgentProvider != AgentProvider.Codex)
        {
            return Result<RecordCodexAccountUsageStopCommandResult>.Failure(Error.Conflict("attempts.not_eligible", "Only a Codex Agent attempt can record an account-usage stop."));
        }

        if (attempt.Status != AttemptStatus.Running || attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result<RecordCodexAccountUsageStopCommandResult>.Failure(Error.Conflict(
                "attempts.not_eligible", "Only an undispatched, running Agent attempt can record an account-usage stop."));
        }

        // The attempt's own immutable snapshot is the only authority for the threshold; it is read from the refreshed entity, the very
        // instance the domain transition validates against, and nothing the caller supplied confers one.
        var reading = attempt.ReadAgentCodexAccountUsageStopPercent();
        if (reading.IsAbsent)
        {
            return Result<RecordCodexAccountUsageStopCommandResult>.Failure(Error.Conflict(NotClaimedCode, "This attempt did not claim an account-usage stop."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        AgentCodexAccountUsageDecision decision;
        if (reading.Value is not { } threshold)
        {
            decision = CodexAccountUsageStopPolicy.UnusableThreshold();
        }
        else if (command.Facts is not { } facts)
        {
            decision = CodexAccountUsageStopPolicy.NoEvidence(threshold);
        }
        else if (facts.AttemptId != attempt.Id)
        {
            return Result<RecordCodexAccountUsageStopCommandResult>.Failure(Error.Conflict(GuardMismatchCode, "The guard facts do not belong to this attempt."));
        }
        else
        {
            // Facts prepared for another threshold, or against another launch tuple than the one now vetted, are not evidence about
            // this attempt's snapshot or this invocation: they resolve as unavailable evidence, never as their own windows.
            var tuple = await CodexAccountUsageStopGate.ReadLaunchTupleAsync(dbContext, cancellationToken);
            var evaluation = facts.ThresholdPercent != threshold
                || tuple is null || tuple.ExecutablePath != facts.ExecutablePath || tuple.ScriptPath != facts.ScriptPath
                    ? new CodexAccountUsageStopEvaluation(CodexAccountUsageStopPolicy.NoEvidence(threshold))
                    : CodexAccountUsageStopPolicy.Evaluate(facts, threshold, nowUtc);
            if (evaluation.StopDecision is not { } stopDecision)
            {
                return Result<RecordCodexAccountUsageStopCommandResult>.Failure(Error.Conflict(NotApplicableCode, "The account-usage guard permits this attempt, so it is not stopped."));
            }

            decision = stopDecision;
        }

        attempt.CompleteAgentAccountUsageStop(decision, nowUtc);
        var completed = RunEvent.Record(
            Guid.NewGuid(),
            command.RunId,
            command.AttemptId,
            RunEventType.AgentAttemptCompleted,
            ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome.ToString() }),
            nowUtc);
        dbContext.Events.Add(completed);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<RecordCodexAccountUsageStopCommandResult>.Success(
            new RecordCodexAccountUsageStopCommandResult(attempt.Status, attempt.AgentOutcome!.Value, completed.Sequence));
    }
}
