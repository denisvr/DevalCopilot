using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ResolvePlanningSequenceStepPreparation;

public sealed class ResolvePlanningSequenceStepPreparationCommandHandler(
    IDevalCopilotDbContext dbContext,
    IPlanningSequenceStepProbe stepProbe,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ResolvePlanningSequenceStepPreparationCommand, Result<ResolvePlanningSequenceStepPreparationCommandResult>>
{
    public async Task<Result<ResolvePlanningSequenceStepPreparationCommandResult>> HandleAsync(
        ResolvePlanningSequenceStepPreparationCommand command, CancellationToken cancellationToken)
    {
        var ticket = command.Ticket;

        // The independent read comes first and through its own connection: a cancellation or an exception never establishes that the
        // claim did not commit.
        var probed = await stepProbe.CheckAsync(ticket.StepId, cancellationToken);

        var runId = await dbContext.PlanningSequences.AsNoTracking()
            .Where(candidate => candidate.Id == ticket.SequenceId)
            .Select(candidate => (Guid?)candidate.RunId)
            .SingleOrDefaultAsync(cancellationToken);
        if (runId is not { } run)
        {
            return Result<ResolvePlanningSequenceStepPreparationCommandResult>.Failure(PlanningSequenceErrors.NotFound());
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The run row, never the sequence row: an ended sequence is immutable by database guard, and even a no-op write to it is refused.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == run)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Result<ResolvePlanningSequenceStepPreparationCommandResult>.Failure(PlanningSequenceErrors.NotFound());
        }

        var sequence = await dbContext.PlanningSequences.SingleAsync(candidate => candidate.Id == ticket.SequenceId, cancellationToken);
        await dbContext.Entry(sequence).ReloadAsync(cancellationToken);
        var step = await dbContext.PlanningSequenceSteps.SingleAsync(candidate => candidate.Id == ticket.StepId, cancellationToken);
        await dbContext.Entry(step).ReloadAsync(cancellationToken);

        if (probed == PlanningSequenceStepProbeResult.Claimed
            || step.State != PlanningSequenceStepState.Reserved
            || step.PreparationOwnerToken != ticket.OwnerToken
            || sequence.State != PlanningSequenceState.PreparingStep)
        {
            // The claim committed, or a Stop, an expiry or a restart already settled this step: nothing is written.
            return Result<ResolvePlanningSequenceStepPreparationCommandResult>.Success(
                new ResolvePlanningSequenceStepPreparationCommandResult(sequence.State, step.State, Changed: false));
        }

        var nowUtc = timeProvider.GetUtcNow();
        var detail = PlanningSequenceReason.NormalizeDetail(command.FailureCode);
        string reason;
        if (probed == PlanningSequenceStepProbeResult.NotClaimed)
        {
            reason = ReasonFor(command.FailureCode);
            step.Revoke(reason, nowUtc);
        }
        else
        {
            reason = PlanningSequenceReason.PreparationUnresolved;
            step.MarkUnresolved(reason, nowUtc);
        }

        sequence.End(PlanningSequenceState.NeedsAttention, reason, detail, nowUtc);
        var ended = PlanningSequenceEvents.Ended(sequence, nowUtc);
        dbContext.Events.Add(ended);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<ResolvePlanningSequenceStepPreparationCommandResult>.Failure(PlanningSequenceErrors.PersistenceFailed());
        }

        await NotifyAsync(run, ended.Sequence, cancellationToken);
        return Result<ResolvePlanningSequenceStepPreparationCommandResult>.Success(
            new ResolvePlanningSequenceStepPreparationCommandResult(sequence.State, step.State, Changed: true));
    }

    /// <summary>The fixed coordinator reason of an absent claim. The original stable error code is preserved separately as the detail,
    /// so a capacity, lease, budget or source refusal never hides its own reason.</summary>
    private static string ReasonFor(string? failureCode) => failureCode switch
    {
        PlanningSequenceErrors.StepExpiredCode => PlanningSequenceReason.PreparationExpired,
        PlanningSequenceErrors.StepSourceChangedCode => PlanningSequenceReason.AuthorityChanged,
        PlanningSequenceErrors.StepAuthorityLostCode or PlanningSequenceErrors.StepInputMismatchCode
            or PlanningSequenceErrors.PersistenceFailedCode or "attempts.persistence_failed" => PlanningSequenceReason.PreparationFailed,
        _ => PlanningSequenceReason.PreparationRefused,
    };

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded
    /// transition into a reported failure. Cancellation still propagates.</summary>
    private async Task NotifyAsync(Guid runId, long sequence, CancellationToken cancellationToken)
    {
        if (eventNotifier is null)
        {
            return;
        }

        try
        {
            await eventNotifier.NotifyRunAdvancedAsync(runId, sequence, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The event is durable; a later cursor read delivers it.
        }
    }
}
