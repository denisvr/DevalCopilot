using System.Text.Json;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The dispatch-time authority of an operation linked to a local execution sequence (ADR-0060). A dispatch marker validates the exact
/// linked step, pinned source, pinned recipes and settings and the unexpired result deadline: <c>AwaitingStepResult</c> and
/// <c>Stopping</c> (which retains the already claimed operation's grant) are dispatchable; <c>NeedsAttention</c>, an elapsed deadline,
/// an ended, interrupted or superseded sequence and an association that cannot be proved are not. A dispatch marker for an unlinked
/// operation is unaffected. The ordinary workspace and global capacity gates still apply independently and are never exempted.
/// </summary>
internal static class LocalExecutionDispatchGate
{
    internal enum Verdict
    {
        /// <summary>No step names this operation: an ordinary operation, unchanged by any sequence.</summary>
        NotLinked,

        Dispatchable,

        /// <summary>A step names this operation but its authority, deadline, source, pinned set or association does not permit dispatch.</summary>
        Refused,
    }

    internal sealed record Evaluation(Verdict Verdict, LocalExecutionSequence? Sequence);

    public static async Task<Evaluation> EvaluateAgentAsync(
        IDevalCopilotDbContext dbContext, Attempt attempt, DateTimeOffset nowUtc, bool trackSequence, CancellationToken cancellationToken)
    {
        var step = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ReservedOperationId == attempt.Id && candidate.Kind != LocalExecutionStepKind.Verification, cancellationToken);
        if (step is null)
        {
            return new Evaluation(Verdict.NotLinked, null);
        }

        var sequences = trackSequence ? dbContext.LocalExecutionSequences : dbContext.LocalExecutionSequences.AsNoTracking();
        var sequence = await sequences.SingleOrDefaultAsync(candidate => candidate.Id == step.SequenceId, cancellationToken);
        return sequence is not null && await IsAgentDispatchableAsync(dbContext, sequence, step, attempt, nowUtc, cancellationToken)
            ? new Evaluation(Verdict.Dispatchable, sequence)
            : new Evaluation(Verdict.Refused, sequence);
    }

    public static async Task<Evaluation> EvaluateVerificationAsync(
        IDevalCopilotDbContext dbContext, VerificationExecution execution, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var step = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ReservedOperationId == execution.Id && candidate.Kind == LocalExecutionStepKind.Verification, cancellationToken);
        if (step is null)
        {
            return new Evaluation(Verdict.NotLinked, null);
        }

        var sequence = await dbContext.LocalExecutionSequences.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == step.SequenceId, cancellationToken);
        return sequence is not null && await IsVerificationDispatchableAsync(dbContext, sequence, step, execution, nowUtc, cancellationToken)
            ? new Evaluation(Verdict.Dispatchable, sequence)
            : new Evaluation(Verdict.Refused, sequence);
    }

    /// <summary>Requires the exact sequence revision this gate decided against at the dispatch marker's own commit: the marker commits
    /// only while the sequence authority is unchanged, so an expiry, a Stop-drain classification or a restart that committed in between
    /// refuses it.</summary>
    public static void Guard(IDevalCopilotDbContext dbContext, LocalExecutionSequence sequence)
    {
        var property = dbContext.Entry(sequence).Property(candidate => candidate.Revision);
        property.OriginalValue = sequence.Revision;
        property.IsModified = true;
    }

    private static async Task<bool> IsAgentDispatchableAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep step,
        Attempt attempt,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        sequence.RunId == attempt.RunId
        && step.SequenceId == sequence.Id
        && step.State == LocalExecutionSequenceStepState.Claimed
        && step.OperationId == attempt.Id
        && sequence.State is LocalExecutionSequenceState.AwaitingStepResult or LocalExecutionSequenceState.Stopping
        && attempt.Kind == AttemptKind.Agent
        && attempt.AgentResponseContract == LocalExecutionSequencePolicy.ContractOf(step.Kind)
        && attempt.AgentGitWorkspaceId == sequence.WorkspaceId
        && attempt.AgentGitCheckpointId == step.SourceCheckpointId
        && string.Equals(attempt.AgentCheckpointFingerprintSha256, step.SourceFingerprintSha256, StringComparison.Ordinal)
        && step.ResultDeadlineUtc is { } deadline
        && nowUtc < deadline
        && nowUtc < sequence.AdvancementDeadlineUtc
        && await PinnedFactsHoldAsync(dbContext, sequence, cancellationToken);

    private static async Task<bool> IsVerificationDispatchableAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep step,
        VerificationExecution execution,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!(sequence.ProjectId == execution.ProjectId
            && step.SequenceId == sequence.Id
            && step.State == LocalExecutionSequenceStepState.Claimed
            && step.OperationId == execution.Id
            && sequence.State is LocalExecutionSequenceState.AwaitingStepResult or LocalExecutionSequenceState.Stopping
            && execution.GitWorkspaceId == sequence.WorkspaceId
            && execution.GitCheckpointId == step.SourceCheckpointId
            && string.Equals(execution.CheckpointFingerprintSha256, step.SourceFingerprintSha256, StringComparison.Ordinal)
            && step.RecipeOrdinal is not null
            && step.ResultDeadlineUtc is { } deadline
            && nowUtc < deadline
            && nowUtc < sequence.AdvancementDeadlineUtc))
        {
            return false;
        }

        var pinned = await dbContext.LocalExecutionSequenceRecipes.AsNoTracking().SingleOrDefaultAsync(
            recipe => recipe.SequenceId == sequence.Id && recipe.Ordinal == step.RecipeOrdinal, cancellationToken);
        return pinned is not null
            && pinned.VerificationCommandId == execution.VerificationCommandId
            && pinned.TimeoutSeconds == execution.TimeoutSeconds
            && string.Equals(pinned.CommandName, execution.CommandName, StringComparison.Ordinal)
            && string.Equals(pinned.ExecutablePath, execution.ExecutablePath, StringComparison.Ordinal)
            && string.Equals(pinned.ArgumentsJson, JsonSerializer.Serialize(execution.Arguments), StringComparison.Ordinal)
            && await PinnedFactsHoldAsync(dbContext, sequence, cancellationToken);
    }

    /// <summary>The pinned whole recipe set and the pinned run settings still hold.</summary>
    private static async Task<bool> PinnedFactsHoldAsync(
        IDevalCopilotDbContext dbContext, LocalExecutionSequence sequence, CancellationToken cancellationToken) =>
        await LocalExecutionStepAuthority.RecipesHoldAsync(dbContext, sequence, cancellationToken)
        && string.Equals(
            await LocalExecutionSettingsSnapshot.ReadAsync(dbContext, sequence.RunId, cancellationToken),
            sequence.SettingsSnapshot,
            StringComparison.Ordinal);
}
