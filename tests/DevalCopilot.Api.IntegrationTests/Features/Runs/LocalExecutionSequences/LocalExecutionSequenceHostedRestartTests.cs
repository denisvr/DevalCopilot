using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences.LocalExecutionSequenceHostedAssertions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>ADR-0060 restart over the same database and artifacts: nothing resumes, redispatches, adopts or releases. A dispatched
/// operation without a recorded result stays exactly as recorded with its partial output untouched, a late result of the original
/// process cannot reopen the ended sequence, and a proved undispatched claim is closed by the dedicated refusal without its adapter.</summary>
public sealed class LocalExecutionSequenceHostedRestartTests
{
    [Fact]
    public async Task An_unresolved_dispatched_operation_survives_a_restart_untouched_and_never_resumes_even_when_its_late_result_arrives()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.ClaudeHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        Assert.True(await harness.Process.ClaudeEntered.WaitAsync(TimeSpan.FromSeconds(60)));
        var operation = (await harness.StepsAsync(armed.SequenceId)).Single().OperationId!.Value;
        var partial = harness.ArtifactStore.GetPartialPath(lineage.RunId, operation, ArtifactPurpose.AgentStandardOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        await File.WriteAllTextAsync(partial, "PARTIAL-OUTPUT-OF-A-PROCESS-THAT-MAY-STILL-RUN");

        await harness.RestartAsync();

        var interrupted = await harness.WaitForSequenceAsync(armed.SequenceId, _ => true);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, interrupted.State);
        Assert.Equal(LocalExecutionSequenceReason.HostRestarted, interrupted.ReasonCode);
        var step = Assert.Single(await harness.StepsAsync(armed.SequenceId));
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, step.State);
        var attempt = await harness.InDbAsync(db => db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == operation));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.NotNull(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentOutcome);
        Assert.Equal("PARTIAL-OUTPUT-OF-A-PROCESS-THAT-MAY-STILL-RUN", await File.ReadAllTextAsync(partial));
        Assert.Empty(await harness.InDbAsync(db => db.Artifacts.AsNoTracking().Where(artifact => artifact.AttemptId == operation
            && artifact.Purpose == ArtifactPurpose.AgentStandardOutput).ToListAsync()));

        // The new host starts everything: no duplicate invocation, successor or resumption appears.
        await harness.StartHostsAsync();
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(1, harness.Process.ClaudeInvocations);
        Assert.Equal(0, harness.Process.VerificationInvocations);
        Assert.Single(await harness.StepsAsync(armed.SequenceId));
        Assert.Equal(AttemptStatus.Running, (await harness.InDbAsync(db => db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == operation))).Status);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await harness.WaitForSequenceAsync(armed.SequenceId, _ => true)).State);

        // The original process returns late into a stopped host (its supervisor records nothing): the ended sequence never reopens.
        harness.Process.ClaudeHold.SetResult();
        await Task.Delay(TimeSpan.FromSeconds(4));
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await harness.WaitForSequenceAsync(armed.SequenceId, _ => true)).State);
        Assert.Single(await harness.StepsAsync(armed.SequenceId));
        Assert.Equal(1, harness.Process.ClaudeInvocations);
        Assert.Equal(0, harness.Process.VerificationInvocations);
        Assert.Equal(0, harness.Review.InvocationCount);
        Assert.Empty(await harness.ExecutionsAsync(lineage.ProjectId));
    }

    [Fact]
    public async Task A_proved_undispatched_linked_claim_is_closed_with_the_dedicated_refusal_and_its_adapter_is_never_called()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        var armed = await harness.ArmAsync(lineage);

        // Only the coordinator runs: it claims the implementation, but no supervisor ever dispatches it.
        await harness.StartHostsAsync(coordinator: true, supervisors: false);
        await harness.WaitUntilAsync(
            db => db.Attempts.AsNoTracking().AnyAsync(attempt => attempt.RunId == lineage.RunId
                && attempt.AgentResponseContract == AgentResponseContract.ImplementationReport),
            "the linked implementation claim");
        var operation = (await harness.StepsAsync(armed.SequenceId)).Single().OperationId!.Value;

        await harness.RestartAsync();
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await harness.WaitForSequenceAsync(armed.SequenceId, _ => true)).State);
        await harness.StartHostsAsync(coordinator: false);
        await harness.WaitUntilAsync(
            async db => (await db.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == operation)).Status != AttemptStatus.Running,
            "the dedicated refusal of the undispatched claim");

        var attempt = await harness.InDbAsync(db => db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == operation));
        Assert.Equal(AgentOutcome.LocalExecutionSequenceDispatchRefused, attempt.AgentOutcome);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.GetAgentProcessExecutionEvidence());
        Assert.Equal(0, harness.Process.ClaudeInvocations);
        Assert.Empty(await harness.InDbAsync(db => db.CollaborationMessages.AsNoTracking().Where(message => message.AttemptId == operation).ToListAsync()));
        Assert.Single(await harness.StepsAsync(armed.SequenceId));
    }

    [Fact]
    public async Task Independent_runs_and_projects_are_unaffected_while_one_sequence_holds_its_fence()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        var other = await harness.SeedLineageAsync();
        harness.Process.ClaudeHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        Assert.True(await harness.Process.ClaudeEntered.WaitAsync(TimeSpan.FromSeconds(60)));

        // The held sequence blocks only its own project's manual verification.
        var blocked = await harness.SendAsync(new DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution
            .ClaimVerificationExecutionCommand(lineage.ProjectId, lineage.RecipeIds[0], lineage.CheckpointId));
        Assert.True(blocked.IsFailure);
        Assert.Empty(await harness.ExecutionsAsync(lineage.ProjectId));

        var free = await harness.SendAsync(new DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution
            .ClaimVerificationExecutionCommand(other.ProjectId, other.RecipeIds[0], other.CheckpointId));
        Assert.True(free.IsSuccess, free.IsFailure ? free.Errors[0].Code : null);
        await harness.WaitUntilAsync(
            async db => (await db.VerificationExecutions.AsNoTracking().SingleAsync(execution => execution.ProjectId == other.ProjectId)).Status
                == DevalCopilot.Domain.Features.Projects.VerificationExecutionStatus.Passed,
            "the independent project's manual verification");
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, (await harness.WaitForSequenceAsync(armed.SequenceId, _ => true)).State);

        harness.Process.ClaudeHold.SetResult();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        Assert.Empty(await harness.InDbAsync(db => db.LocalExecutionSequences.AsNoTracking().Where(candidate => candidate.RunId == other.RunId).ToListAsync()));
        Assert.Single(await harness.ExecutionsAsync(other.ProjectId));
    }
}
