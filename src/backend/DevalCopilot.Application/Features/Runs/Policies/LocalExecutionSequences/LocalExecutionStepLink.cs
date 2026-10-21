using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>The confirmed, still-tracked step and sequence of one claim. <see cref="Apply"/> only mutates tracked entities and adds the
/// step-claimed event; the caller's own SaveChanges commits them with the operation, its inputs, artifacts, counters and any budget or
/// grant consumption, so the link is never a separate operation and there is no claim-then-link gap (ADR-0060).</summary>
internal sealed class LocalExecutionStepLink(LocalExecutionSequence sequence, LocalExecutionSequenceStep step)
{
    public Guid StepId => step.Id;

    public Guid SequenceId => sequence.Id;

    public void Apply(IDevalCopilotDbContext dbContext, Guid operationId, TimeSpan timeout, DateTimeOffset nowUtc)
    {
        var deadline = LocalExecutionSequencePolicy.ResultDeadlineOf(nowUtc, timeout, sequence.AdvancementDeadlineUtc)
            ?? throw new InvalidOperationException("The result deadline of a claimed step must be representable.");
        step.MarkClaimed(operationId, nowUtc, deadline);
        sequence.MarkAwaitingResult(nowUtc);
        dbContext.Events.Add(LocalExecutionSequenceEvents.StepClaimed(sequence, step, operationId, nowUtc));
    }
}
