using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;
using DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: normal intake recognizes coherent Abandoned facts as terminal and nothing else. A new run keeps the ordinary
/// creation rules: its own identity, execution number, objective and fresh budgets, and it inherits no plan, grant, approval,
/// message, artifact or consumed slot of the abandoned run, whose history stays exactly as it was.</summary>
public sealed class RunIntakeAfterAbandonmentTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 15, 0, 0, TimeSpan.Zero);

    private async Task<Result<CreateManualRunCommandResult>> CreateManualAsync(
        Guid projectId, string objective = "A different objective", int? attempts = null, int? minutes = null)
    {
        await using var db = fixture.CreateContext();
        return await new CreateManualRunCommandHandler(db, new FixedTimeProvider(Now))
            .HandleAsync(new CreateManualRunCommand(projectId, objective, attempts, minutes), CancellationToken.None);
    }

    private async Task<Result<StartSimulatedRunCommandResult>> StartSimulatedAsync(Guid projectId)
    {
        await using var db = fixture.CreateContext();
        return await new StartSimulatedRunCommandHandler(db, new FixedTimeProvider(Now))
            .HandleAsync(new StartSimulatedRunCommand(projectId, "Demo objective"), CancellationToken.None);
    }

    private async Task AbandonAsync(AbandonmentScene scene)
    {
        var result = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId);
        Assert.True(result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null);
    }

    public static TheoryData<bool> InactiveStates() => [false, true];

    [Theory]
    [MemberData(nameof(InactiveStates))]
    public async Task An_abandoned_manual_run_permits_both_creation_operations_with_a_distinct_identity_and_the_next_number(bool running)
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running);
        await AbandonAsync(scene);

        var manual = await CreateManualAsync(scene.ProjectId);

        Assert.True(manual.IsSuccess, manual.IsFailure ? Assert.Single(manual.Errors).Code : null);
        Assert.NotEqual(scene.RunId, manual.Value.RunId);
        Assert.Equal(2, manual.Value.ExecutionNumber);

        // The new run is itself an active run, so the ordinary rule blocks the next intent until it is terminal.
        var blocked = await StartSimulatedAsync(scene.ProjectId);
        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(blocked.Errors).Code);
    }

    [Fact]
    public async Task An_abandoned_manual_run_permits_the_simulated_creation_too()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture);
        await AbandonAsync(scene);

        var simulated = await StartSimulatedAsync(scene.ProjectId);

        Assert.True(simulated.IsSuccess);
        Assert.Equal(2, simulated.Value.ExecutionNumber);
    }

    [Fact]
    public async Task The_new_run_has_fresh_defaults_or_its_own_chosen_budgets_whatever_the_abandoned_run_consumed()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true, withWorkspace: true);
        await using (var db = fixture.CreateContext())
        {
            // The abandoned run's own ceilings were small and its three claims are spent (a failed attempt keeps its slot).
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE runs SET MaximumAgentAttempts = 3, MaximumAgentInvocationTime = {0} WHERE Id = {1}",
                TimeSpan.FromMinutes(30).Ticks, scene.RunId);
            for (var slot = 1; slot <= 3; slot++)
            {
                var attempt = Attempt.ClaimAgent(
                    Guid.NewGuid(), scene.RunId, slot, scene.WorkspaceId!.Value, scene.CheckpointId!.Value, new string('b', 64), Guid.NewGuid(),
                    TimeSpan.FromMinutes(10), 262144, 524288, AbandonmentScene.Created, slot);
                attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, AbandonmentScene.Created);
                db.Attempts.Add(attempt);
            }

            await db.SaveChangesAsync();
        }

        await AbandonAsync(scene);
        var before = await ReadHistoryAsync(scene.RunId);

        var defaults = await CreateManualAsync(scene.ProjectId, "Fresh objective");
        Assert.True(defaults.IsSuccess);
        await using (var db = fixture.CreateContext())
        {
            var fresh = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == defaults.Value.RunId);
            Assert.Equal(Run.DefaultMaximumAgentAttempts, fresh.MaximumAgentAttempts);
            Assert.Equal(Run.DefaultMaximumAgentInvocationTime, fresh.MaximumAgentInvocationTime);
            Assert.Equal(RunLifecycle.Created, fresh.Lifecycle);
            Assert.Equal(RunExecutionMode.ManualAgent, fresh.ExecutionMode);
            Assert.Equal("Fresh objective", fresh.Objective);
            Assert.Null(fresh.AbandonmentReason);
            Assert.Null(fresh.AbandonedAtUtc);
            Assert.Equal(0, await db.Attempts.CountAsync(candidate => candidate.RunId == fresh.Id));
            Assert.Equal(0, await db.Artifacts.CountAsync(candidate => candidate.RunId == fresh.Id));
            Assert.Equal(0, await db.CollaborationMessages.CountAsync(candidate => candidate.RunId == fresh.Id));
            Assert.Equal(0, await db.PlanningImplementationAuthorizations.CountAsync(candidate => candidate.RunId == fresh.Id));
            Assert.Equal(0, await db.ReviewCorrectionAuthorizations.CountAsync(candidate => candidate.RunId == fresh.Id));
            Assert.Equal(0, await db.LocalCommitOperations.CountAsync(candidate => candidate.RunId == fresh.Id));
            Assert.Equal([RunEventType.RunStarted], await db.Events.Where(candidate => candidate.RunId == fresh.Id).Select(candidate => candidate.EventType).ToListAsync());
        }

        // The abandoned run's facts, spent slots and history are exactly as they were.
        Assert.Equal(before, await ReadHistoryAsync(scene.RunId));
        Assert.Equal(3, before.Attempts);
        Assert.Equal(3, before.MaximumAgentAttempts);
    }

    [Fact]
    public async Task A_chosen_budget_applies_to_the_new_run_only()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture);
        await AbandonAsync(scene);

        var chosen = await CreateManualAsync(scene.ProjectId, attempts: 4, minutes: 45);

        Assert.True(chosen.IsSuccess);
        await using var db = fixture.CreateContext();
        var fresh = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == chosen.Value.RunId);
        Assert.Equal(4, fresh.MaximumAgentAttempts);
        Assert.Equal(TimeSpan.FromMinutes(45), fresh.MaximumAgentInvocationTime);
        var abandoned = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(Run.DefaultMaximumAgentAttempts, abandoned.MaximumAgentAttempts);
    }

    [Fact]
    public async Task Several_abandoned_runs_in_a_row_each_permit_the_next_objective()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture);
        await AbandonAsync(scene);
        var second = await CreateManualAsync(scene.ProjectId, "Second");
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, second.Value.RunId)).IsSuccess);

        var third = await CreateManualAsync(scene.ProjectId, "Third");

        Assert.True(third.IsSuccess);
        Assert.Equal(3, third.Value.ExecutionNumber);
    }

    // ---- incoherent facts never authorize another intent ---------------------------------------------------------------------

    public static TheoryData<string> Breakages() =>
    [
        "no-reason", "no-time", "no-event", "agent-event", "other-reason-event", "legacy-mode", "simulated-mode", "unrecognized-mode",
        "active-participant", "second-event",
    ];

    private async Task BreakAsync(AbandonmentScene scene, string name)
    {
        switch (name)
        {
            case "no-reason": await scene.SqlAsync("UPDATE runs SET AbandonmentReason = NULL WHERE Id = {0}", scene.RunId); break;
            case "no-time": await scene.SqlAsync("UPDATE runs SET AbandonedAtUtc = NULL WHERE Id = {0}", scene.RunId); break;
            case "no-event": await scene.SqlAsync("DELETE FROM events WHERE EventType = 'run.abandoned' AND RunId = {0}", scene.RunId); break;
            case "agent-event": await scene.SqlAsync("UPDATE events SET ActorKind = 'Agent' WHERE EventType = 'run.abandoned' AND RunId = {0}", scene.RunId); break;
            case "other-reason-event":
                await scene.SqlAsync(
                    "UPDATE events SET PayloadJson = {0} WHERE EventType = 'run.abandoned' AND RunId = {1}",
                    "{\"reason\":\"Forged\"}", scene.RunId);
                break;
            case "legacy-mode": await scene.SetRunModeAsync("0"); break;
            case "simulated-mode": await scene.SetRunModeAsync("1"); break;
            case "unrecognized-mode": await scene.SetRunModeAsync("'x'"); break;
            case "active-participant": await scene.SqlAsync("UPDATE runs SET ActiveParticipantKind = 'Orchestrator' WHERE Id = {0}", scene.RunId); break;
            case "second-event":
                await scene.SqlAsync(
                    "INSERT INTO events (Id, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc) "
                    + "SELECT {0}, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc FROM events "
                    + "WHERE EventType = 'run.abandoned' AND RunId = {1}",
                    Guid.NewGuid(), scene.RunId);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Breakages))]
    public async Task An_incoherent_abandoned_run_blocks_both_creation_operations_and_changes_nothing(string breakage)
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        await AbandonAsync(scene);
        await BreakAsync(scene, breakage);
        var before = await scene.SnapshotAsync();

        var manual = await CreateManualAsync(scene.ProjectId);
        var simulated = await StartSimulatedAsync(scene.ProjectId);

        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(manual.Errors).Code);
        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(simulated.Errors).Code);
        Assert.Equal(before, await scene.SnapshotAsync());
    }

    [Fact]
    public async Task The_abandoned_lifecycle_never_hides_another_active_run_of_the_project()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture);
        await AbandonAsync(scene);
        await using (var db = fixture.CreateContext())
        {
            var project = await db.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
            db.Runs.Add(Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Still active", Now));
            await db.SaveChangesAsync();
        }

        var blocked = await CreateManualAsync(scene.ProjectId);

        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(blocked.Errors).Code);
    }

    [Fact]
    public async Task Another_projects_abandoned_run_changes_nothing_for_this_project()
    {
        var abandonedProject = await AbandonmentScene.CreateAsync(fixture);
        await AbandonAsync(abandonedProject);
        var activeProject = await AbandonmentScene.CreateAsync(fixture);

        var blocked = await CreateManualAsync(activeProject.ProjectId);
        var allowed = await CreateManualAsync(abandonedProject.ProjectId);

        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(blocked.Errors).Code);
        Assert.True(allowed.IsSuccess);
    }

    private sealed record History(
        string Lifecycle, string? Reason, DateTimeOffset? AbandonedAtUtc, int MaximumAgentAttempts, int Attempts, int Events, string Payload);

    private async Task<History> ReadHistoryAsync(Guid runId)
    {
        await using var db = fixture.CreateContext();
        var run = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        var events = await db.Events.AsNoTracking().Where(candidate => candidate.RunId == runId).OrderBy(candidate => candidate.Sequence).ToListAsync();
        return new History(
            run.Lifecycle.ToString(), run.AbandonmentReason, run.AbandonedAtUtc, run.MaximumAgentAttempts,
            await db.Attempts.CountAsync(candidate => candidate.RunId == runId), events.Count,
            string.Join("|", events.Select(candidate => candidate.EventType + ":" + candidate.PayloadJson)));
    }
}
