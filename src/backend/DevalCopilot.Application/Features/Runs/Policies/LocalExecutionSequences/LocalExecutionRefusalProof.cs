using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The one proof that a linked operation was claimed by exactly one local execution sequence and was never dispatched, required before
/// either family's dedicated dispatch refusal is recorded by startup or by the live recorders (ADR-0060). A UUID or link state alone is
/// never enough: the sequence, run, project, workspace, source checkpoint and fingerprint, step kind and response contract must all agree,
/// the step must name the operation as both its reserved and its claimed identity, and no dispatch marker or process evidence may
/// exist. Anything weaker is an unproved association: no outcome is fabricated, no event is written against another run and no
/// ownership is released (the shared ownership policies keep reading it as quarantined or Unavailable).
/// </summary>
internal static class LocalExecutionRefusalProof
{
    public static async Task<bool> ProvesUndispatchedAgentClaimAsync(
        IDevalCopilotDbContext dbContext, Attempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.Kind != AttemptKind.Agent
            || attempt.Status != AttemptStatus.Running
            || attempt.AgentDispatchedAtUtc is not null
            || attempt.AgentProcessOutcome is not null
            || attempt.AgentProcessExitCode is not null
            || attempt.AgentProcessDuration is not null)
        {
            return false;
        }

        var step = await dbContext.LocalExecutionSequenceSteps.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.ReservedOperationId == attempt.Id && candidate.Kind != LocalExecutionStepKind.Verification,
            cancellationToken);
        if (step is null
            || step.OperationId != attempt.Id
            || step.State is not (LocalExecutionSequenceStepState.Claimed or LocalExecutionSequenceStepState.Concluded))
        {
            return false;
        }

        var sequence = await dbContext.LocalExecutionSequences.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == step.SequenceId, cancellationToken);
        if (sequence is null
            || sequence.RunId != attempt.RunId
            || attempt.AgentGitWorkspaceId != sequence.WorkspaceId
            || attempt.AgentGitCheckpointId != step.SourceCheckpointId
            || !string.Equals(attempt.AgentCheckpointFingerprintSha256, step.SourceFingerprintSha256, StringComparison.Ordinal)
            || attempt.AgentResponseContract != LocalExecutionSequencePolicy.ContractOf(step.Kind))
        {
            return false;
        }

        return await dbContext.Runs.AsNoTracking()
            .AnyAsync(run => run.Id == attempt.RunId && run.ProjectId == sequence.ProjectId, cancellationToken);
    }

    public static async Task<bool> ProvesUndispatchedVerificationClaimAsync(
        IDevalCopilotDbContext dbContext, VerificationExecution execution, CancellationToken cancellationToken)
    {
        if (execution.Status != VerificationExecutionStatus.Running
            || execution.DispatchedAtUtc is not null
            || execution.Outcome is not null
            || execution.ExitCode is not null
            || execution.CompletionFingerprintSha256 is not null)
        {
            return false;
        }

        var step = await dbContext.LocalExecutionSequenceSteps.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.ReservedOperationId == execution.Id && candidate.Kind == LocalExecutionStepKind.Verification,
            cancellationToken);
        if (step is null
            || step.OperationId != execution.Id
            || step.RecipeOrdinal is not { } recipeOrdinal
            || step.State is not (LocalExecutionSequenceStepState.Claimed or LocalExecutionSequenceStepState.Concluded))
        {
            return false;
        }

        var sequence = await dbContext.LocalExecutionSequences.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == step.SequenceId, cancellationToken);
        if (sequence is null
            || sequence.ProjectId != execution.ProjectId
            || execution.GitWorkspaceId != sequence.WorkspaceId
            || execution.GitCheckpointId != step.SourceCheckpointId
            || !string.Equals(execution.CheckpointFingerprintSha256, step.SourceFingerprintSha256, StringComparison.Ordinal))
        {
            return false;
        }

        return await dbContext.LocalExecutionSequenceRecipes.AsNoTracking().AnyAsync(
            recipe => recipe.SequenceId == sequence.Id
                && recipe.Ordinal == recipeOrdinal
                && recipe.VerificationCommandId == execution.VerificationCommandId,
            cancellationToken)
            && await dbContext.Runs.AsNoTracking().AnyAsync(
                run => run.Id == sequence.RunId && run.ProjectId == sequence.ProjectId, cancellationToken);
    }
}
