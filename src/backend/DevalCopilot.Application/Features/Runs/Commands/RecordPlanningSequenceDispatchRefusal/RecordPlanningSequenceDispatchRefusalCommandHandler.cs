using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordPlanningSequenceDispatchRefusal;

public sealed class RecordPlanningSequenceDispatchRefusalCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordPlanningSequenceDispatchRefusalCommand, Result>
{
    private const string NotEligibleCode = "attempts.not_eligible";

    public async Task<Result> HandleAsync(RecordPlanningSequenceDispatchRefusalCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The transaction's first statement: a no-op write that takes the database write lock. The proof below is read after it, so a
        // sequence transition that committed first is seen here and the recorded outcome is atomic with the proof.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Result.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);
        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Agent
            || attempt.AgentResponseContract is not (AgentResponseContract.Proposal
                or AgentResponseContract.CriticalReview
                or AgentResponseContract.ChallengeResolution))
        {
            return Result.Failure(Error.Conflict("attempts.not_planning_family", "The attempt is not a planning-sequence contract attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result.Failure(Error.Conflict(
                NotEligibleCode, "Only an undispatched, running planning attempt can record a planning-sequence dispatch refusal."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        var evaluation = await PlanningSequenceDispatchGate.EvaluateAsync(
            dbContext, attempt, nowUtc, trackSequence: false, cancellationToken);
        if (evaluation.Verdict != PlanningSequenceDispatchGate.Verdict.Refused || !await IsProvedLinkAsync(attempt, evaluation, cancellationToken))
        {
            // Unlinked, still dispatchable, or an association that cannot be proved: no outcome is fabricated.
            return Result.Failure(Error.Conflict(NotEligibleCode, "No provable planning-sequence refusal of this attempt exists."));
        }

        attempt.CompletePlanningSequenceDispatchRefused(nowUtc);
        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            command.RunId,
            command.AttemptId,
            RunEventType.AgentAttemptCompleted,
            ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome.ToString() }),
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>The refusal is recorded only for a coherent association: the step names this exact attempt as its claimed attempt of the
    /// same run and sequence. Anything weaker (for example a reserved identity that matches no claimed link) is not provable.</summary>
    private async Task<bool> IsProvedLinkAsync(
        Attempt attempt, PlanningSequenceDispatchGate.Evaluation evaluation, CancellationToken cancellationToken)
    {
        if (evaluation.Sequence is not { } sequence || sequence.RunId != attempt.RunId)
        {
            return false;
        }

        return await dbContext.PlanningSequenceSteps.AsNoTracking().AnyAsync(
            step => step.SequenceId == sequence.Id
                && step.ReservedAttemptId == attempt.Id
                && step.AttemptId == attempt.Id
                && (step.State == PlanningSequenceStepState.Claimed || step.State == PlanningSequenceStepState.Concluded),
            cancellationToken);
    }
}
