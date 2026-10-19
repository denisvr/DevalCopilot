using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>The confirmed, still-tracked step and sequence of one claim. <see cref="Apply"/> only mutates tracked entities and adds the
/// step-claimed event; the caller's own SaveChanges commits them with the attempt, so the link is never a separate operation.</summary>
internal sealed class PlanningSequenceStepLink(PlanningSequence sequence, PlanningSequenceStep step)
{
    public Guid StepId => step.Id;

    public void Apply(IDevalCopilotDbContext dbContext, Guid attemptId, DateTimeOffset nowUtc)
    {
        step.MarkClaimed(attemptId, nowUtc);
        sequence.MarkAwaitingResult(nowUtc);
        dbContext.Events.Add(PlanningSequenceEvents.StepClaimed(sequence, step, attemptId, nowUtc));
    }
}
