using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences.LocalExecutionSequenceHostedAssertions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>ADR-0060 refusals proved through the real hosts: a linked verification claim whose recorded arguments drifted from the pinned
/// recipe is refused at the dispatch marker without any process call, and a pending linked claim whose run identity was moved to another
/// valid run is never terminalized by the real startup closure.</summary>
public sealed class LocalExecutionSequenceHostedRefusalTests
{
    [Fact]
    public async Task A_claim_whose_arguments_drifted_from_the_pinned_recipe_is_refused_with_zero_process_calls()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        var armed = await harness.ArmAsync(lineage);

        // The verification supervisor is held back: the coordinator claims the first recipe, which stays undispatched.
        await harness.StartHostsAsync(verification: false);
        await harness.WaitUntilAsync(db => db.VerificationExecutions.AnyAsync(), "the linked verification claim");
        var execution = Assert.Single(await harness.ExecutionsAsync(lineage.ProjectId));
        Assert.Null(execution.DispatchedAtUtc);
        await using (var db = harness.OpenContext())
        {
            var tracked = await db.VerificationExecutions.SingleAsync(candidate => candidate.Id == execution.Id);
            var property = db.Entry(tracked).Property(candidate => candidate.Arguments);
            property.CurrentValue = ["--drifted-argument", "test"];
            property.IsModified = true;
            await db.SaveChangesAsync();
        }

        await harness.StartVerificationSupervisorAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        var refused = await harness.InDbAsync(db => db.VerificationExecutions.AsNoTracking().SingleAsync(candidate => candidate.Id == execution.Id));
        Assert.Equal(VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, refused.Status);
        Assert.Null(refused.DispatchedAtUtc);
        Assert.Null(refused.Outcome);
        Assert.Null(refused.ExitCode);
        Assert.Null(refused.CompletionFingerprintSha256);
        Assert.Equal(0, harness.Process.VerificationInvocations);

        // No successor, diagnosis or review exists, and the never-dispatched refusal no longer owns the project.
        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(["Implementation", "Verification@1.0"], Trail(steps));
        Assert.Equal(0, harness.Diagnosis.InvocationCount);
        Assert.Equal(0, harness.Review.InvocationCount);
        Assert.Equal(1, harness.Process.ClaudeInvocations);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Single(await harness.ExecutionsAsync(lineage.ProjectId));
        Assert.Equal(0, harness.Process.VerificationInvocations);
        var resultCheckpoint = (await harness.AgentAttemptsAsync(lineage.RunId))
            .Single(attempt => attempt.Id == steps[0].OperationId).AgentResultGitCheckpointId!.Value;
        var manual = await harness.SendAsync(new ClaimVerificationExecutionCommand(lineage.ProjectId, lineage.RecipeIds[0], resultCheckpoint));
        Assert.True(manual.IsSuccess, manual.IsFailure ? manual.Errors[0].Code : null);
    }

    [Fact]
    public async Task A_pending_linked_claim_moved_to_another_run_is_not_terminalized_by_the_real_startup_closure()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        var other = await harness.SeedLineageAsync();
        var armed = await harness.ArmAsync(lineage);

        // Only the coordinator runs: the implementation is claimed and linked, never dispatched.
        await harness.StartHostsAsync(coordinator: true, supervisors: false);
        await harness.WaitUntilAsync(
            db => db.Attempts.AsNoTracking().AnyAsync(attempt => attempt.RunId == lineage.RunId
                && attempt.AgentResponseContract == AgentResponseContract.ImplementationReport),
            "the linked implementation claim");
        var operation = (await harness.StepsAsync(armed.SequenceId)).Single().OperationId!.Value;
        await harness.StopHostsAsync();
        await using (var db = harness.OpenContext())
        {
            var tracked = await db.Attempts.SingleAsync(candidate => candidate.Id == operation);
            var property = db.Entry(tracked).Property(candidate => candidate.RunId);
            property.CurrentValue = other.RunId;
            property.IsModified = true;
            await db.SaveChangesAsync();
        }

        var otherEventsBefore = await harness.InDbAsync(db => db.Events.CountAsync(runEvent => runEvent.RunId == other.RunId));
        await harness.RestartAsync();

        var attempt = await harness.InDbAsync(db => db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == operation));
        Assert.Equal(other.RunId, attempt.RunId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Equal(otherEventsBefore, await harness.InDbAsync(db => db.Events.CountAsync(runEvent => runEvent.RunId == other.RunId)));
        Assert.Empty(await harness.InDbAsync(db => db.Events.AsNoTracking()
            .Where(runEvent => runEvent.RunId == other.RunId && runEvent.EventType.StartsWith("local_execution_sequence")).ToListAsync()));
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await harness.WaitForSequenceAsync(armed.SequenceId, _ => true)).State);

        // Even a running supervisor never dispatches or refuses the foreign claim.
        await harness.StartHostsAsync(coordinator: false);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0, harness.Process.ClaudeInvocations);
        var after = await harness.InDbAsync(db => db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == operation));
        Assert.Equal(AttemptStatus.Running, after.Status);
        Assert.Null(after.AgentOutcome);
    }
}
