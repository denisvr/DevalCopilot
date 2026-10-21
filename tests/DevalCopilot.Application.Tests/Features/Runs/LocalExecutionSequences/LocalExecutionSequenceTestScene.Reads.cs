using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>Independent, untracked reads of the scene's durable state; each opens its own context.</summary>
internal sealed partial class LocalExecutionSequenceTestScene
{
    public async Task<LocalExecutionSequence> ReadSequenceAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.LocalExecutionSequences.AsNoTracking().SingleAsync(candidate => candidate.RunId == RunId);
    }

    public async Task<LocalExecutionSequence?> FindSequenceAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.LocalExecutionSequences.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.RunId == RunId);
    }

    public async Task<List<LocalExecutionSequenceStep>> ReadStepsAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(step => context.LocalExecutionSequences.Any(sequence => sequence.Id == step.SequenceId && sequence.RunId == RunId))
            .OrderBy(step => step.Ordinal).ToListAsync();
    }

    public async Task<List<LocalExecutionSequenceRecipe>> ReadPinnedRecipesAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.LocalExecutionSequenceRecipes.AsNoTracking()
            .Where(recipe => context.LocalExecutionSequences.Any(sequence => sequence.Id == recipe.SequenceId && sequence.RunId == RunId))
            .OrderBy(recipe => recipe.Ordinal).ToListAsync();
    }

    public async Task<List<Attempt>> ReadAttemptsAsync()
    {
        await using var context = Fixture.CreateContext();
        return (await context.Attempts.AsNoTracking().Where(attempt => attempt.RunId == RunId).ToListAsync())
            .OrderBy(attempt => attempt.AttemptNumber).ToList();
    }

    /// <summary>The attempts of the four contracts a local execution sequence claims (implementation, review, correction, diagnosis);
    /// the plan lineage attempts the scene seeded are not included.</summary>
    public async Task<List<Attempt>> ReadLocalAttemptsAsync() => (await ReadAttemptsAsync()).Where(attempt => attempt.AgentResponseContract is
        AgentResponseContract.ImplementationReport or AgentResponseContract.ImplementationReview or AgentResponseContract.ReviewCorrection
        or AgentResponseContract.VerificationDiagnosis).ToList();

    public async Task<Attempt> ReadAttemptAsync(Guid attemptId)
    {
        await using var context = Fixture.CreateContext();
        return await context.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId);
    }

    public async Task<List<VerificationExecution>> ReadExecutionsAsync()
    {
        await using var context = Fixture.CreateContext();
        return (await context.VerificationExecutions.AsNoTracking().Where(execution => execution.ProjectId == ProjectId).ToListAsync())
            .OrderBy(execution => execution.ExecutionNumber).ToList();
    }

    public async Task<VerificationExecution> ReadExecutionAsync(Guid executionId)
    {
        await using var context = Fixture.CreateContext();
        return await context.VerificationExecutions.AsNoTracking().SingleAsync(execution => execution.Id == executionId);
    }

    public async Task<List<CollaborationMessage>> ReadMessagesAsync(CollaborationMessageType? type = null)
    {
        await using var context = Fixture.CreateContext();
        var messages = await context.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == RunId && (type == null || message.Type == type)).ToListAsync();
        return messages.OrderBy(message => message.Sequence).ToList();
    }

    public async Task<List<Guid>> ReadInputsAsync(Guid attemptId)
    {
        await using var context = Fixture.CreateContext();
        return await context.AttemptInputMessages.AsNoTracking().Where(input => input.AttemptId == attemptId)
            .OrderBy(input => input.Sequence).Select(input => input.CollaborationMessageId).ToListAsync();
    }

    public async Task<int> CountEventsAsync(string eventType)
    {
        await using var context = Fixture.CreateContext();
        return await context.Events.AsNoTracking().CountAsync(candidate => candidate.RunId == RunId && candidate.EventType == eventType);
    }

    public async Task<GitCheckpoint> ReadLatestCheckpointAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.GitCheckpoints.AsNoTracking().Where(checkpoint => checkpoint.WorkspaceId == WorkspaceId)
            .OrderByDescending(checkpoint => checkpoint.CheckpointNumber).FirstAsync();
    }

    public async Task<Run> ReadRunAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.Runs.AsNoTracking().SingleAsync(run => run.Id == RunId);
    }

    /// <summary>The count of every kind of durable work a claim consumes: attempts, executions, sealed manifests and Git captures.</summary>
    public async Task<(int Attempts, int Executions, int Seals, int Captures)> CountWorkAsync()
    {
        await using var context = Fixture.CreateContext();
        return (
            await context.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == RunId),
            await context.VerificationExecutions.AsNoTracking().CountAsync(execution => execution.ProjectId == ProjectId),
            Artifacts.SealCount,
            Evidence.Captures);
    }
}
