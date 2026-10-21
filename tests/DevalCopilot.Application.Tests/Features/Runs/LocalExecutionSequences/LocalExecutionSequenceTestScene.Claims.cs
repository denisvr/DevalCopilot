using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>Claim-level helpers of the scene: driving a cycle up to the reservation of a chosen step kind (so the ticket is still
/// unclaimed), forging tickets, and the durable footprint a claim must either leave completely or not at all.</summary>
internal sealed partial class LocalExecutionSequenceTestScene
{
    /// <summary>Everything one claim may consume or write, so a refusal can be proved to have left no residue.</summary>
    public sealed record Footprint(
        int Attempts,
        int Executions,
        int Inputs,
        int Artifacts,
        int VerificationEvidence,
        int Events,
        int Escalations,
        int ReviewCorrectionEscalations,
        int DiagnosisCorrectionEscalations,
        int ReviewCorrectionAuthorizations,
        int ConsumedPlanGrants,
        long SequenceRevision,
        int ClaimedSteps,
        int Seals,
        int DeletedSeals);

    /// <summary>
    /// Drives ticks (claim, dispatch, scripted result) until an observation reserves the <paramref name="occurrence"/>-th step of
    /// <paramref name="kind"/>, and returns that ticket UNCLAIMED. Fails when the cycle ends first.
    /// </summary>
    public async Task<LocalExecutionStepTicket> ReserveUntilAsync(
        Guid sequenceId, LocalExecutionStepKind kind, LocalExecutionStepScript? script = null, int occurrence = 1)
    {
        script ??= new LocalExecutionStepScript();
        for (var tick = 0; tick < 120; tick++)
        {
            var ticket = await ObserveReservingAsync(sequenceId);
            if (ticket.Kind == kind && --occurrence == 0)
            {
                return ticket;
            }

            var claim = await ClaimAsync(ticket);
            Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
            await CompleteClaimedAsync(ticket, script);
            Clock.Advance(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException($"The cycle never reserved the requested {kind} step.");
    }

    /// <summary>Dispatches and records a claimed step exactly as <see cref="StepAsync"/> does after its claim.</summary>
    public async Task CompleteClaimedAsync(LocalExecutionStepTicket ticket, LocalExecutionStepScript? script = null)
    {
        script ??= new LocalExecutionStepScript();
        var row = (await ReadStepsAsync()).Single(candidate => candidate.Id == ticket.StepId);
        var step = new LocalExecutionStepContext(
            ticket.Kind, row.PassNumber, row.RecipeOrdinal, ticket.Ordinal, ticket.ReservedOperationId, script.Findings);
        var result = script.Decide(step);
        if (result == ScriptedResultKind.LeaveUndispatched)
        {
            return;
        }

        await DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        if (result != ScriptedResultKind.LeaveRunning)
        {
            await RecordAsync(step, result);
        }
    }

    /// <summary>A ticket identical to <paramref name="real"/> except for the members given.</summary>
    public static LocalExecutionStepTicket Forge(
        LocalExecutionStepTicket real,
        Guid? ownerToken = null,
        Guid? reservedOperationId = null,
        Guid? sourceCheckpointId = null,
        Guid? reportMessageId = null,
        Guid? subjectAttemptId = null,
        LocalExecutionStepKind? kind = null,
        int? ordinal = null,
        Guid? stepId = null,
        Guid? sequenceId = null) => new(
        sequenceId ?? real.SequenceId,
        stepId ?? real.StepId,
        ordinal ?? real.Ordinal,
        kind ?? real.Kind,
        reservedOperationId ?? real.ReservedOperationId,
        ownerToken ?? real.OwnerToken,
        sourceCheckpointId ?? real.SourceCheckpointId,
        reportMessageId ?? real.ReportMessageId,
        subjectAttemptId ?? real.SubjectAttemptId,
        real.RecipeId);

    public async Task<Footprint> FootprintAsync()
    {
        await using var context = Fixture.CreateContext();
        var sequence = await context.LocalExecutionSequences.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.RunId == RunId);
        return new Footprint(
            await context.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == RunId),
            await context.VerificationExecutions.AsNoTracking().CountAsync(execution => execution.ProjectId == ProjectId),
            await context.AttemptInputMessages.AsNoTracking().CountAsync(input => context.Attempts.Any(attempt => attempt.Id == input.AttemptId && attempt.RunId == RunId)),
            await context.Artifacts.AsNoTracking().CountAsync(artifact => artifact.RunId == RunId),
            await context.AttemptVerificationEvidence.AsNoTracking().CountAsync(
                evidence => context.Attempts.Any(attempt => attempt.Id == evidence.AttemptId && attempt.RunId == RunId)),
            await context.Events.AsNoTracking().CountAsync(candidate => candidate.RunId == RunId),
            await context.CollaborationMessages.AsNoTracking().CountAsync(
                message => message.RunId == RunId && message.Type == CollaborationMessageType.Escalation),
            await context.ReviewCorrectionEscalations.AsNoTracking().CountAsync(escalation => escalation.RunId == RunId),
            await context.DiagnosisCorrectionEscalations.AsNoTracking().CountAsync(escalation => escalation.RunId == RunId),
            await context.ReviewCorrectionAuthorizations.AsNoTracking().CountAsync(authorization => authorization.RunId == RunId),
            await context.PlanningImplementationAuthorizations.AsNoTracking().CountAsync(
                grant => grant.RunId == RunId && grant.ConsumedByAttemptId != null),
            sequence?.Revision ?? 0,
            sequence is null
                ? 0
                : await context.LocalExecutionSequenceSteps.AsNoTracking().CountAsync(
                    step => step.SequenceId == sequence.Id && step.OperationId != null),
            Artifacts.SealCount,
            Artifacts.DeletedSealedFiles.Count);
    }

    /// <summary>The ordered finding messages (ReviewFinding) of the run, oldest first.</summary>
    public async Task<List<CollaborationMessage>> ReadFindingMessagesAsync() => await ReadMessagesAsync(CollaborationMessageType.ReviewFinding);

    /// <summary>The number of Agent attempts of the correction contract (both origins) the run claimed.</summary>
    public async Task<int> CountCorrectionAttemptsAsync() => (await ReadAttemptsAsync())
        .Count(attempt => attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection);
}
