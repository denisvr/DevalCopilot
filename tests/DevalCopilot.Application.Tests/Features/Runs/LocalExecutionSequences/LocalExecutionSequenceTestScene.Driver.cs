using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Commands.ClaimLocalExecutionSequenceStep;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>Deterministic driving of a whole cycle: arm, then repeat observe, claim, dispatch and record until the sequence ends.</summary>
internal sealed partial class LocalExecutionSequenceTestScene
{
    /// <summary>One driven coordinator tick: the ticket the observation reserved (null when none), the claim outcome and the result the
    /// script recorded.</summary>
    public sealed record DrivenStep(
        LocalExecutionStepTicket? Ticket,
        Result<ClaimLocalExecutionSequenceStepCommandResult>? Claim,
        ScriptedResultKind? Recorded,
        LocalExecutionSequenceState State);

    /// <summary>
    /// One tick as the coordinator performs it: an observation (which concludes a terminal step and reserves the next), the one claim of
    /// the reserved ticket, and, when the claim committed, the dispatch marker and the scripted result. A refused claim is settled by the
    /// real resolve command with its error code. No ticket means the observation changed nothing worth driving.
    /// </summary>
    public async Task<DrivenStep> StepAsync(Guid sequenceId, LocalExecutionStepScript? script = null)
    {
        script ??= new LocalExecutionStepScript();
        var observed = await ObserveAsync(sequenceId);
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Errors[0].Code : null);
        if (observed.Value.ReservedStep is not { } ticket)
        {
            return new DrivenStep(null, null, null, observed.Value.State);
        }

        var claim = await ClaimAsync(ticket);
        if (claim.IsFailure)
        {
            await ResolveAsync(ticket, claim.Errors[0].Code);
            return new DrivenStep(ticket, claim, null, (await ReadSequenceAsync()).State);
        }

        var step = new LocalExecutionStepContext(
            ticket.Kind, 0, null, ticket.Ordinal, ticket.ReservedOperationId, script.Findings);
        var row = (await ReadStepsAsync()).Single(candidate => candidate.Id == ticket.StepId);
        step = step with { PassNumber = row.PassNumber, RecipeOrdinal = row.RecipeOrdinal };
        var kind = script.Decide(step);
        if (kind != ScriptedResultKind.LeaveUndispatched)
        {
            await DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
            if (kind != ScriptedResultKind.LeaveRunning)
            {
                await RecordAsync(step, kind);
            }
        }

        return new DrivenStep(ticket, claim, kind, (await ReadSequenceAsync()).State);
    }

    /// <summary>Drives ticks until the sequence is no longer live (or a tick reserves nothing), at most <paramref name="maximumTicks"/>.</summary>
    public async Task<LocalExecutionSequence> DriveAsync(
        Guid sequenceId, LocalExecutionStepScript? script = null, int maximumTicks = 120)
    {
        for (var tick = 0; tick < maximumTicks; tick++)
        {
            var driven = await StepAsync(sequenceId, script);
            if (driven.Ticket is null && !LocalExecutionSequencePolicy.IsAdvancing(driven.State))
            {
                break;
            }

            if (driven.Ticket is null && driven.State != LocalExecutionSequenceState.Stopping)
            {
                // Nothing reserved while still advancing: an outstanding operation (left running) holds the cycle.
                break;
            }

            if (driven.Ticket is not null && driven.Claim is { IsFailure: true })
            {
                break;
            }

            Clock.Advance(TimeSpan.FromSeconds(1));
        }

        return await ReadSequenceAsync();
    }

    /// <summary>Arms the plan and drives the whole cycle to its end with the script; returns the ended sequence.</summary>
    public async Task<LocalExecutionSequence> ArmAndDriveAsync(Guid planProposalMessageId, LocalExecutionStepScript? script = null)
    {
        var armed = await ArmAsync(planProposalMessageId);
        return await DriveAsync(armed.SequenceId, script);
    }

    /// <summary>The (kind, pass, recipe) trace of every non-revoked step in order, for exact membership assertions.</summary>
    public async Task<List<(LocalExecutionStepKind Kind, int Pass, int? Recipe)>> TraceAsync() =>
        (await ReadStepsAsync()).Where(step => step.State != LocalExecutionSequenceStepState.Revoked)
            .Select(step => (step.Kind, step.PassNumber, step.RecipeOrdinal)).ToList();
}
