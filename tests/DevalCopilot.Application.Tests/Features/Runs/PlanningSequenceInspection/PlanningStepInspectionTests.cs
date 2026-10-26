using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.ResolvePlanningSequenceStepPreparation;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningStepInspection;
using DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequenceInspection;

/// <summary>The selected association of one recorded planning step (ADR-0068) over real file-backed SQLite: the fixed non-disclosing 404
/// for an unknown run, a run without a sequence and a missing or foreign step; NoLinkedAttempt only for the coherent Reserved, Revoked and
/// Unresolved shapes and never by discovering an attempt through a reserved identity; and the existing immutable Agent header for every
/// linked step whose association is coherent, including a Running, failed, refused, interrupted or message-less attempt. The read is
/// select-only and observes nothing externally.</summary>
public sealed class PlanningStepInspectionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task An_unknown_run_a_run_without_a_sequence_a_missing_step_and_a_foreign_step_are_one_fixed_not_found()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.DriveStepsAsync(2);
        var foreign = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await foreign.Scene.ObserveReservingAsync(foreign.SequenceId);
        var foreignStep = (await foreign.RowsAsync()).Single().Id;
        var legacy = await PlanningSequenceTestScene.CreateAsync(fixture, mode: RunExecutionMode.Legacy);
        var ownStep = (await harness.RowsAsync())[0].Id;

        var results = new[]
        {
            await harness.TryStepAsync(ownStep, Guid.NewGuid()),
            await harness.TryStepAsync(ownStep, legacy.RunId),
            await harness.TryStepAsync(Guid.NewGuid()),
            await harness.TryStepAsync(foreignStep),
            await harness.TryStepAsync(Guid.Empty),
        };

        Assert.All(results, result =>
        {
            Assert.True(result.IsFailure);
            Assert.Equal(PlanningSequenceInspectionErrors.NotFoundCode, result.Errors[0].Code);
        });
        Assert.Single(results.Select(result => result.Errors[0]).Distinct());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Every_linked_step_returns_its_exact_step_and_only_the_immutable_attempt_header(int count)
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var attempts = await harness.DriveStepsAsync(count);
        var index = await harness.IndexAsync();
        var stored = await harness.Scene.ReadAttemptsAsync();

        foreach (var member in index.Steps)
        {
            var answer = await harness.StepAsync(member.StepId);

            Assert.Equal(PlanningStepInspectionStatus.Recorded, answer.Status);
            Assert.Equal(harness.Scene.RunId, answer.RunId);
            Assert.Equal(harness.SequenceId, answer.SequenceId);
            Assert.Equal(member.StepId, answer.StepId);
            Assert.Equal(member, answer.Step);
            var agent = answer.Agent!;
            var attempt = stored.Single(candidate => candidate.Id == attempts[member.Ordinal]);
            Assert.Equal(attempt.Id, agent.Id);
            Assert.Equal(attempt.AttemptNumber, agent.AttemptNumber);
            Assert.Equal(member.Role, agent.Role);
            Assert.Equal(member.Provider, agent.Provider);
            Assert.Equal(member.ResponseContract, agent.ResponseContract);
        }
    }

    [Fact]
    public async Task A_running_attempt_that_emitted_nothing_and_a_dispatched_one_are_inspectable_without_a_producing_message()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var (_, claim) = await harness.Scene.ReserveAndClaimAsync(harness.SequenceId);
        var stepId = (await harness.RowsAsync()).Single().Id;

        var running = await harness.StepAsync(stepId);

        Assert.Equal(PlanningStepInspectionStatus.Recorded, running.Status);
        Assert.Equal(claim.AttemptId, running.Agent!.Id);
        Assert.Equal(AttemptStatus.Running, (await harness.Scene.ReadAttemptsAsync()).Single().Status);
        Assert.Equal(0, await MessageCountAsync());

        await using (var db = fixture.CreateContext())
        {
            var attempt = await db.Attempts.SingleAsync(candidate => candidate.Id == claim.AttemptId);
            attempt.MarkAgentDispatched(harness.Scene.Clock.GetUtcNow());
            await db.SaveChangesAsync();
        }

        var dispatched = await harness.StepAsync(stepId);

        Assert.Equal(PlanningStepInspectionStatus.Recorded, dispatched.Status);
        Assert.Equal(claim.AttemptId, dispatched.Agent!.Id);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("invalid_output")]
    [InlineData("interrupted")]
    [InlineData("dispatch_refused")]
    public async Task A_failed_refused_interrupted_or_no_output_attempt_stays_inspectable_when_its_association_is_coherent(string shape)
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var (_, claim) = await harness.Scene.ReserveAndClaimAsync(harness.SequenceId);
        switch (shape)
        {
            case "failed":
                await harness.Scene.CompleteStepAsync(claim.AttemptId, StepResult.ProviderFailed);
                break;
            case "invalid_output":
                await harness.Scene.CompleteStepAsync(claim.AttemptId, StepResult.InvalidOutput);
                break;
            default:
                await using (var db = fixture.CreateContext())
                {
                    var attempt = await db.Attempts.SingleAsync(candidate => candidate.Id == claim.AttemptId);
                    if (shape == "interrupted")
                    {
                        attempt.Interrupt(harness.Scene.Clock.GetUtcNow());
                    }
                    else
                    {
                        attempt.CompletePlanningSequenceDispatchRefused(harness.Scene.Clock.GetUtcNow());
                    }

                    await db.SaveChangesAsync();
                }

                break;
        }

        await harness.Scene.ObserveAsync(harness.SequenceId);
        var stepId = (await harness.RowsAsync()).Single().Id;

        var answer = await harness.StepAsync(stepId);

        Assert.Equal(PlanningStepInspectionStatus.Recorded, answer.Status);
        Assert.Equal(claim.AttemptId, answer.Agent!.Id);
        Assert.Equal(AgentRole.Planner, answer.Agent.Role);
        Assert.Equal(0, await MessageCountAsync());
    }

    [Fact]
    public async Task A_reserved_step_and_a_revoked_step_are_no_linked_attempt_with_their_step_and_no_agent()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.Scene.ObserveReservingAsync(harness.SequenceId);
        var stepId = (await harness.RowsAsync()).Single().Id;

        var reserved = await harness.StepAsync(stepId);

        Assert.Equal(PlanningStepInspectionStatus.NoLinkedAttempt, reserved.Status);
        Assert.Null(reserved.Step);
        Assert.Null(reserved.Agent);
        Assert.Equal(stepId, reserved.StepId);
        Assert.Equal(harness.SequenceId, reserved.SequenceId);

        await harness.Scene.StopAsync(harness.SequenceId);

        var revoked = await harness.StepAsync(stepId);

        Assert.Equal(PlanningStepInspectionStatus.NoLinkedAttempt, revoked.Status);
        Assert.Null(revoked.Step);
        Assert.Null(revoked.Agent);
    }

    [Fact]
    public async Task An_unresolved_step_stays_uncertainty_with_no_link_and_the_read_releases_and_writes_nothing()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var ticket = await harness.Scene.ObserveReservingAsync(harness.SequenceId);
        await using (var db = fixture.CreateContext())
        {
            await new ResolvePlanningSequenceStepPreparationCommandHandler(db, new UnresolvedProbe(), harness.Scene.Clock).HandleAsync(
                new ResolvePlanningSequenceStepPreparationCommand(ticket, "attempts.persistence_unresolved"), CancellationToken.None);
        }

        var before = await harness.ChangeFingerprintAsync();
        var stepId = (await harness.RowsAsync()).Single().Id;
        var log = new StatementLog();

        var answer = await harness.StepAsync(stepId, null, log);

        Assert.Equal(PlanningStepInspectionStatus.NoLinkedAttempt, answer.Status);
        Assert.Null(answer.Agent);
        log.AssertReadOnly();
        Assert.Equal(before, await harness.ChangeFingerprintAsync());
        Assert.Equal(PlanningSequenceStepState.Unresolved, (await harness.RowsAsync()).Single().State);
    }

    [Fact]
    public async Task An_attempt_with_the_reserved_identity_that_the_step_never_linked_is_never_discovered_or_substituted()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var ticket = await harness.Scene.ObserveReservingAsync(harness.SequenceId);
        await harness.Scene.StopAsync(harness.SequenceId);
        var other = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var (_, otherClaim) = await other.Scene.ReserveAndClaimAsync(other.SequenceId);

        // An attempt row of this run now carries the revoked step's reserved identity, as if its preparation had committed unseen.
        await harness.CorruptAsync(
            "UPDATE attempts SET Id = $p0, RunId = $p1, AgentGitWorkspaceId = $p2, AgentGitCheckpointId = $p3 WHERE Id = $p4",
            [ticket.ReservedAttemptId.ToString().ToUpperInvariant(), harness.Scene.RunId.ToString().ToUpperInvariant(),
                harness.Scene.WorkspaceId.ToString().ToUpperInvariant(), harness.Scene.CheckpointId.ToString().ToUpperInvariant(),
                otherClaim.AttemptId.ToString().ToUpperInvariant()]);
        await using (var db = fixture.CreateContext())
        {
            Assert.True(await db.Attempts.AnyAsync(attempt => attempt.Id == ticket.ReservedAttemptId && attempt.RunId == harness.Scene.RunId));
        }

        var stepId = (await harness.RowsAsync()).Single().Id;

        var answer = await harness.StepAsync(stepId);

        Assert.Equal(PlanningStepInspectionStatus.NoLinkedAttempt, answer.Status);
        Assert.Null(answer.Agent);
        Assert.Null(answer.Step);
    }

    [Fact]
    public async Task A_selected_read_is_select_only_observes_nothing_externally_and_never_selects_message_or_artifact_columns()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.DriveStepsAsync(3);
        var stepId = (await harness.RowsAsync())[2].Id;
        var before = await harness.ChangeFingerprintAsync();
        var captures = harness.Scene.Evidence.Captures;
        var seals = harness.Scene.Artifacts.Seals;
        var log = new StatementLog();

        var answer = await harness.StepAsync(stepId, null, log);

        Assert.Equal(PlanningStepInspectionStatus.Recorded, answer.Status);
        log.AssertReadOnly();
        log.AssertNeverSelects(
            "ObjectiveSha256", "DetailCode", "PreparationOwnerToken", "StructuredContentJson", "\"Summary\"", "WorkspacePath",
            "AgentPrompt", "AgentSessionId", "ArtifactPath");
        Assert.Equal(before, await harness.ChangeFingerprintAsync());
        Assert.Equal(captures, harness.Scene.Evidence.Captures);
        Assert.Equal(seals, harness.Scene.Artifacts.Seals);
    }

    private async Task<int> MessageCountAsync()
    {
        await using var db = fixture.CreateContext();
        return await db.CollaborationMessages.AsNoTracking().CountAsync();
    }

    private sealed class UnresolvedProbe : IPlanningSequenceStepProbe
    {
        public Task<PlanningSequenceStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken) =>
            Task.FromResult(PlanningSequenceStepProbeResult.Unresolved);
    }
}
