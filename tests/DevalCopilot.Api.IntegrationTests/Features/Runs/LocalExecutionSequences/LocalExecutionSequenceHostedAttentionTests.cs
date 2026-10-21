using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Runs.Commands.StopLocalExecutionSequence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences.LocalExecutionSequenceHostedAssertions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>The ADR-0060 NeedsAttention and Stop branches through the real hosts: a recipe without exit proof stops everything and keeps
/// its owner quarantined, invalid provider output and capacity refusal end advancement without consuming anything further, Stop lets the
/// claimed operation finish and starts no successor, and the two-hour deadline closes successor authority.</summary>
public sealed class LocalExecutionSequenceHostedAttentionTests
{
    public static TheoryData<string> NoExitProofBehaviors => ["timeout", "cancelled", "throws"];

    [Theory]
    [MemberData(nameof(NoExitProofBehaviors))]
    public async Task A_recipe_without_exit_proof_needs_attention_and_its_owner_stays_quarantined(string behavior)
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.Verification = call => call.RecipeNumber != 1
            ? LocalExecutionSequenceVerificationBehavior.Passes
            : behavior switch
            {
                "timeout" => LocalExecutionSequenceVerificationBehavior.TimesOut,
                "cancelled" => LocalExecutionSequenceVerificationBehavior.IsCancelled,
                _ => LocalExecutionSequenceVerificationBehavior.AdapterThrows,
            };
        var armed = await harness.ArmAsync(lineage);

        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(["Implementation", "Verification@1.0"], Trail(steps));
        Assert.Equal(1, harness.Process.VerificationInvocations);
        Assert.Equal(0, harness.Diagnosis.InvocationCount);
        Assert.Equal(0, harness.Review.InvocationCount);
        Assert.Equal(1, harness.Process.ClaudeInvocations);
        var execution = Assert.Single(await harness.ExecutionsAsync(lineage.ProjectId));
        Assert.NotEqual(VerificationExecutionStatus.Running, execution.Status);
        Assert.NotEqual(VerificationExecutionOutcome.Exited, execution.Outcome);
        var resultCheckpoint = (await harness.AgentAttemptsAsync(lineage.RunId)).Single(a => a.Id == steps[0].OperationId).AgentResultGitCheckpointId!.Value;

        // The terminal row has no exit proof, so its exact operation stays an effective owner and a manual claim is refused.
        var before = (await harness.ExecutionsAsync(lineage.ProjectId)).Count;
        var manual = await harness.SendAsync(new ClaimVerificationExecutionCommand(lineage.ProjectId, lineage.RecipeIds[1], resultCheckpoint));
        Assert.True(manual.IsFailure);
        Assert.Equal(before, (await harness.ExecutionsAsync(lineage.ProjectId)).Count);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(1, harness.Process.VerificationInvocations);
        Assert.Equal(before, (await harness.ExecutionsAsync(lineage.ProjectId)).Count);
    }

    [Fact]
    public async Task Invalid_provider_output_ends_advancement_without_a_repair_a_retry_or_a_verification()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.ClaudeAnswers.Enqueue("this is not an implementation report");
        var armed = await harness.ArmAsync(lineage);

        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(["Implementation"], Trail(await harness.StepsAsync(sequence.Id)));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(1, harness.Process.ClaudeInvocations);
        Assert.Equal(0, harness.Process.VerificationInvocations);
        Assert.Equal(0, harness.Review.InvocationCount);
        var attempt = Assert.Single(await harness.AgentAttemptsAsync(lineage.RunId),
            candidate => candidate.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, attempt.AgentOutcome);
        Assert.Null(attempt.AgentRepairSourceAttemptId);
    }

    [Fact]
    public async Task Capacity_limit_one_with_another_mutating_owner_needs_attention_and_consumes_no_operation()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        var armed = await harness.ArmAsync(lineage);
        await using (var db = harness.OpenContext())
        {
            var other = await LocalExecutionSequenceApiSeed.SeedAsync(db);
            var owner = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), other.RunId, 3, other.WorkspaceId, other.CheckpointId, LocalExecutionSequenceApiSeed.Fingerprint,
                Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, DateTimeOffset.UtcNow.AddSeconds(-15), 3);
            owner.MarkAgentDispatched(DateTimeOffset.UtcNow.AddSeconds(-10));
            db.Attempts.Add(owner);
            db.ExecutionCapacityConfigurations.Add(ExecutionCapacityConfigurationRevision.Record(2, 1));
            await db.SaveChangesAsync();
        }

        var attemptsBefore = (await harness.AgentAttemptsAsync(lineage.RunId)).Count;
        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(attemptsBefore, (await harness.AgentAttemptsAsync(lineage.RunId)).Count);
        Assert.Empty(await harness.ExecutionsAsync(lineage.ProjectId));
        Assert.Equal(0, harness.Process.ClaudeInvocations);
        Assert.DoesNotContain(await harness.StepsAsync(sequence.Id), step => step.OperationId is not null);
    }

    [Fact]
    public async Task Stop_after_the_current_operation_lets_the_claimed_operation_finish_and_starts_no_successor()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.ClaudeHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        Assert.True(await harness.Process.ClaudeEntered.WaitAsync(TimeSpan.FromSeconds(60)));

        var stop = await harness.SendAsync(new StopLocalExecutionSequenceCommand(lineage.RunId, armed.SequenceId));
        Assert.True(stop.IsSuccess, stop.IsFailure ? stop.Errors[0].Code : null);
        var again = await harness.SendAsync(new StopLocalExecutionSequenceCommand(lineage.RunId, armed.SequenceId));
        Assert.True(again.IsSuccess);
        Assert.Equal(LocalExecutionSequenceState.Stopping, (await harness.WaitForSequenceAsync(armed.SequenceId, _ => true)).State);
        Assert.Equal(0, harness.Process.VerificationInvocations);

        // Nothing is cancelled: the held process is released by the test alone and its result is recorded in the ordinary way.
        harness.Process.ClaudeHold.SetResult();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.Stopped, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.StoppedAfterOperation, sequence.ReasonCode);
        Assert.Equal(["Implementation"], Trail(await harness.StepsAsync(sequence.Id)));
        var attempt = Assert.Single(await harness.AgentAttemptsAsync(lineage.RunId),
            candidate => candidate.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(AgentOutcome.Implemented, attempt.AgentOutcome);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, harness.Process.VerificationInvocations);
        Assert.Equal(1, harness.Process.ClaudeInvocations);
    }

    [Fact]
    public async Task The_advancement_deadline_closes_successor_authority_and_a_late_result_cannot_reopen_the_sequence()
    {
        var clock = new AgentSupervisions.OffsetClock();
        await using var harness = new LocalExecutionSequenceHostedHarness(clock);
        var lineage = await harness.SeedLineageAsync();
        harness.Process.ClaudeHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        Assert.True(await harness.Process.ClaudeEntered.WaitAsync(TimeSpan.FromSeconds(60)));

        clock.Advance(TimeSpan.FromHours(3));
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Contains(sequence.ReasonCode, new[]
        {
            LocalExecutionSequenceReason.AdvancementDeadlineElapsed, LocalExecutionSequenceReason.ResultDeadlineElapsed,
        });

        harness.Process.ClaudeHold.SetResult();
        await harness.WaitUntilAsync(
            async db => (await db.Attempts.AsNoTracking().Where(attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport)
                .Select(attempt => attempt.Status).SingleAsync()) != AttemptStatus.Running,
            "the late result to be recorded in the ordinary way");
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await harness.WaitForSequenceAsync(armed.SequenceId, _ => true)).State);
        Assert.Equal(["Implementation"], Trail(await harness.StepsAsync(armed.SequenceId)));
        Assert.Equal(0, harness.Process.VerificationInvocations);
    }
}
