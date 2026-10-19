using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>
/// The dispatch-time authority of a coordinator-linked attempt (ADR-0055). The dispatch marker's atomic gate validates the attempt's exact
/// sequence, step, pinned source and unexpired result deadline: <c>AwaitingStepResult</c> and <c>Stopping</c> (which retains its
/// already claimed step's grant) are dispatchable; <c>NeedsAttention</c>, an elapsed deadline, an ended or interrupted sequence and an
/// association that cannot be proved are not. A dispatch marker for an unlinked attempt is unaffected.
/// </summary>
internal static class PlanningSequenceDispatchGate
{
    internal enum Verdict
    {
        /// <summary>No step names this attempt: an ordinary attempt, unchanged by the sequence.</summary>
        NotLinked,

        Dispatchable,

        /// <summary>A step names this attempt but its authority, deadline, source or association does not permit dispatch.</summary>
        Refused,
    }

    internal sealed record Evaluation(Verdict Verdict, PlanningSequence? Sequence);

    public static async Task<Evaluation> EvaluateAsync(
        IDevalCopilotDbContext dbContext, Attempt attempt, DateTimeOffset nowUtc, bool trackSequence, CancellationToken cancellationToken)
    {
        var step = await dbContext.PlanningSequenceSteps.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ReservedAttemptId == attempt.Id, cancellationToken);
        if (step is null)
        {
            return new Evaluation(Verdict.NotLinked, null);
        }

        var sequences = trackSequence ? dbContext.PlanningSequences : dbContext.PlanningSequences.AsNoTracking();
        var sequence = await sequences.SingleOrDefaultAsync(candidate => candidate.Id == step.SequenceId, cancellationToken);
        return sequence is not null && IsDispatchable(sequence, step, attempt, nowUtc)
            ? new Evaluation(Verdict.Dispatchable, sequence)
            : new Evaluation(Verdict.Refused, sequence);
    }

    public static bool IsDispatchable(PlanningSequence sequence, PlanningSequenceStep step, Attempt attempt, DateTimeOffset nowUtc) =>
        sequence.RunId == attempt.RunId
        && step.SequenceId == sequence.Id
        && step.State == PlanningSequenceStepState.Claimed
        && step.AttemptId == attempt.Id
        && sequence.State is PlanningSequenceState.AwaitingStepResult or PlanningSequenceState.Stopping
        && attempt.Kind == AttemptKind.Agent
        && attempt.AgentRole == PlanningSequencePolicy.RoleOf(step.Ordinal)
        && attempt.AgentResponseContract == PlanningSequencePolicy.ContractOf(step.Ordinal)
        && attempt.AgentGitWorkspaceId == sequence.WorkspaceId
        && attempt.AgentGitCheckpointId == sequence.CheckpointId
        && string.Equals(attempt.AgentCheckpointFingerprintSha256, sequence.FingerprintSha256, StringComparison.Ordinal)
        && PlanningSequencePolicy.ResultDeadlineOf(attempt.ClaimedAtUtc, attempt.AgentTimeout) is { } deadline
        && nowUtc < deadline;

    /// <summary>Requires the exact sequence revision this gate decided against at the dispatch marker's own commit: the marker commits
    /// only while the sequence authority is unchanged, so an expiry, a Stop-drain classification or a restart that committed in between
    /// refuses it. The tracked sequence is aligned to its stored revision and marked as the compared value, like the execution mode.</summary>
    public static void Guard(IDevalCopilotDbContext dbContext, PlanningSequence sequence)
    {
        var property = dbContext.Entry(sequence).Property(candidate => candidate.Revision);
        property.OriginalValue = sequence.Revision;
        property.IsModified = true;
    }
}
