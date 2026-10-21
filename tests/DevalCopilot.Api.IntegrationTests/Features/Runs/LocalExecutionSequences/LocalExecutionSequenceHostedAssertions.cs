using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>The durable facts every hosted local execution sequence scenario proves, read through independent connections.</summary>
internal static class LocalExecutionSequenceHostedAssertions
{
    /// <summary>A compact, ordered rendering of the recorded steps: verification steps carry their pass and recipe ordinal.</summary>
    public static string[] Trail(IEnumerable<LocalExecutionSequenceStep> steps) => steps
        .OrderBy(step => step.Ordinal)
        .Select(step => step.Kind == LocalExecutionStepKind.Verification
            ? $"Verification@{step.PassNumber}.{step.RecipeOrdinal}"
            : step.Kind.ToString())
        .ToArray();

    public static string[] Verifications(int pass, int recipes) =>
        Enumerable.Range(0, recipes).Select(recipe => $"Verification@{pass}.{recipe}").ToArray();

    /// <summary>Every step of an ended cycle is linked to exactly its reserved operation and concluded, with consecutive ordinals.</summary>
    public static void AssertEveryStepConcluded(IReadOnlyList<LocalExecutionSequenceStep> steps)
    {
        Assert.Equal(Enumerable.Range(0, steps.Count), steps.Select(step => step.Ordinal));
        Assert.All(steps, step =>
        {
            Assert.Equal(LocalExecutionSequenceStepState.Concluded, step.State);
            Assert.Equal(step.ReservedOperationId, step.OperationId);
        });
    }

    /// <summary>The exact ExecutionReport the given operation recorded.</summary>
    public static Task<CollaborationMessage> ReportOfAsync(LocalExecutionSequenceHostedHarness harness, Guid? attemptId) =>
        harness.InDbAsync(db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == attemptId && message.Type == CollaborationMessageType.ExecutionReport));

    /// <summary>A correction was claimed with exactly the preceding report and every finding of its source operation in protocol
    /// order, answered every finding once in that order, and its source is the exact review or diagnosis the step recorded.</summary>
    public static async Task AssertCorrectionSelectedEveryFindingAsync(
        LocalExecutionSequenceHostedHarness harness, LocalExecutionSequenceStep correction, LocalExecutionSequenceStep source)
    {
        Assert.Equal(source.OperationId, correction.SubjectAttemptId);
        var findings = await harness.InDbAsync(db => db.CollaborationMessages.AsNoTracking()
            .Where(message => message.AttemptId == source.OperationId && message.Type == CollaborationMessageType.ReviewFinding)
            .OrderBy(message => message.Sequence).Select(message => message.Id).ToListAsync());
        Assert.NotEmpty(findings);
        Assert.Equal(
            [source.ReportMessageId!.Value, .. findings],
            await harness.InDbAsync(db => db.AttemptInputMessages.AsNoTracking().Where(input => input.AttemptId == correction.OperationId)
                .OrderBy(input => input.Sequence).Select(input => input.CollaborationMessageId).ToListAsync()));
        Assert.Equal(
            findings,
            await harness.InDbAsync(db => db.CollaborationMessages.AsNoTracking()
                .Where(message => message.AttemptId == correction.OperationId && message.Type == CollaborationMessageType.RevisionResponse)
                .OrderBy(message => message.Sequence).Select(message => message.InReplyToMessageId!.Value).ToListAsync()));
    }

    /// <summary>The verification steps of one pass ran against exactly the checkpoint the preceding Agent attempt produced.</summary>
    public static async Task AssertPassRanAgainstAsync(
        LocalExecutionSequenceHostedHarness harness, IReadOnlyList<LocalExecutionSequenceStep> steps, int pass, Guid producingAttemptId)
    {
        var checkpoint = (await harness.InDbAsync(db => db.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == producingAttemptId)))
            .AgentResultGitCheckpointId;
        Assert.NotNull(checkpoint);
        var passSteps = steps.Where(step => step.Kind == LocalExecutionStepKind.Verification && step.PassNumber == pass).ToList();
        Assert.NotEmpty(passSteps);
        foreach (var step in passSteps)
        {
            var execution = await harness.InDbAsync(db => db.VerificationExecutions.AsNoTracking().SingleAsync(candidate => candidate.Id == step.OperationId));
            Assert.Equal(checkpoint, execution.GitCheckpointId);
            Assert.Equal(checkpoint, step.SourceCheckpointId);
        }
    }

    /// <summary>The cycle claimed only operations it linked: no unlinked Agent attempt or verification execution exists beyond the
    /// planning lineage the test seeded, and every Agent operation or execution is the exact reserved operation of one step.</summary>
    public static async Task AssertEveryOperationIsLinkedAsync(
        LocalExecutionSequenceHostedHarness harness, LocalExecutionSequenceLineage lineage, IReadOnlyList<LocalExecutionSequenceStep> steps)
    {
        var attempts = await harness.AgentAttemptsAsync(lineage.RunId);
        var executions = await harness.ExecutionsAsync(lineage.ProjectId);
        var linkedAgents = steps.Where(step => step.Kind != LocalExecutionStepKind.Verification).Select(step => step.OperationId!.Value).ToHashSet();
        var linkedExecutions = steps.Where(step => step.Kind == LocalExecutionStepKind.Verification).Select(step => step.OperationId!.Value).ToHashSet();
        Assert.Equal(linkedExecutions, executions.Select(execution => execution.Id).ToHashSet());
        var sequenceAttempts = attempts.Where(attempt => attempt.AgentResponseContract is not (AgentResponseContract.Proposal
            or AgentResponseContract.CriticalReview or AgentResponseContract.ChallengeResolution)).ToList();
        Assert.Equal(linkedAgents, sequenceAttempts.Select(attempt => attempt.Id).ToHashSet());
    }

    /// <summary>Nothing outside the authorized cycle happened: no Human checkpoint approval, no local commit, no extra-correction or
    /// planning-escalation grant, and every process the double saw was either the Claude CLI or a pinned verification recipe.</summary>
    public static async Task AssertNothingBeyondTheCycleAsync(LocalExecutionSequenceHostedHarness harness, bool depthTwo = false)
    {
        Assert.Empty(await harness.InDbAsync(db => db.CheckpointReviews.Where(review => review.ActorKind == ReviewActorKind.Human).ToListAsync()));
        Assert.Empty(await harness.InDbAsync(db => db.LocalCommitOperations.ToListAsync()));
        Assert.Empty(await harness.InDbAsync(db => db.ReviewCorrectionAuthorizations.ToListAsync()));
        Assert.Empty(await harness.InDbAsync(db => db.ReviewCorrectionEscalations.ToListAsync()));
        Assert.Equal(
            depthTwo ? 1 : 0,
            await harness.InDbAsync(db => db.PlanningImplementationAuthorizations.CountAsync()));
        lock (harness.Process.Requests)
        {
            Assert.All(harness.Process.Requests, request => Assert.True(
                request.ExecutablePath == LocalExecutionSequenceHostedHarness.SentinelExecutable
                    || request.ExecutablePath.EndsWith("claude.exe", StringComparison.OrdinalIgnoreCase),
                $"Unexpected process {request.ExecutablePath}."));
        }
    }

    /// <summary>Every recipe ran once per started pass, serially, in the pinned order: the recipe number order within each pass.</summary>
    public static void AssertSerialPinnedOrder(LocalExecutionSequenceHostedHarness harness, int passes, int recipes)
    {
        List<LocalExecutionSequenceVerificationCall> calls;
        lock (harness.Process.VerificationCalls)
        {
            calls = [.. harness.Process.VerificationCalls];
        }

        Assert.Equal(passes * recipes, calls.Count);
        var expected = Enumerable.Range(1, passes).SelectMany(_ => Enumerable.Range(1, recipes));
        Assert.Equal(expected, calls.OrderBy(call => call.Order).Select(call => call.RecipeNumber));
    }

    public static async Task<Dictionary<string, int>> EventCountsAsync(LocalExecutionSequenceHostedHarness harness, Guid runId) =>
        (await harness.InDbAsync(db => db.Events.AsNoTracking().Where(runEvent => runEvent.RunId == runId).Select(runEvent => runEvent.EventType).ToListAsync()))
        .GroupBy(type => type).ToDictionary(group => group.Key, group => group.Count());

    /// <summary>After an end, a quiet period creates no operation, no request and no external call.</summary>
    public static async Task AssertQuietAfterEndAsync(LocalExecutionSequenceHostedHarness harness, LocalExecutionSequenceLineage lineage)
    {
        var attempts = (await harness.AgentAttemptsAsync(lineage.RunId)).Count;
        var executions = (await harness.ExecutionsAsync(lineage.ProjectId)).Count;
        var claude = harness.Process.ClaudeInvocations;
        var verification = harness.Process.VerificationInvocations;
        var review = harness.Review.InvocationCount;
        var diagnosis = harness.Diagnosis.InvocationCount;
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(attempts, (await harness.AgentAttemptsAsync(lineage.RunId)).Count);
        Assert.Equal(executions, (await harness.ExecutionsAsync(lineage.ProjectId)).Count);
        Assert.Equal(claude, harness.Process.ClaudeInvocations);
        Assert.Equal(verification, harness.Process.VerificationInvocations);
        Assert.Equal(review, harness.Review.InvocationCount);
        Assert.Equal(diagnosis, harness.Diagnosis.InvocationCount);
    }
}
