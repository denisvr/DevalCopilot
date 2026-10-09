using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetManualRunAbandonment;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031 on real file-backed SQLite with a populated change tracker: the Run the abandonment mutates is refreshed from the
/// write-locked database, original concurrency values included, before the domain transition. A tracker holding an older copy of the
/// same Run (loaded earlier by the same context) must never turn a newer committed participant, stage or lifecycle into an incoherent
/// or wrongly clocked closure: the closure is coherent and current, or it is refused with nothing written.</summary>
public sealed class AbandonManualRunStaleTrackerTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Created = AbandonmentScene.Created;

    private static readonly DateTimeOffset Now = Created.AddSeconds(100);

    private static string Code<T>(Result<T> result) => Assert.Single(result.Errors).Code;

    /// <summary>Another request commits <paramref name="change"/> to the run after <paramref name="held"/> already tracks an older copy.</summary>
    private async Task CommitCompetingAsync(Guid runId, Action<Run> change)
    {
        await using var competing = fixture.CreateContext();
        var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
        change(run);
        await competing.SaveChangesAsync();
    }

    private static async Task<Result<AbandonManualRunCommandResult>> AbandonAsync(DevalCopilotDbContext held, Guid runId) =>
        await new AbandonManualRunCommandHandler(held, new FixedTimeProvider(Now))
            .HandleAsync(new AbandonManualRunCommand(runId, AbandonmentRaceSupport.Reason), CancellationToken.None);

    private async Task SeedAsync(Guid runId, Action<Run> change) => await CommitCompetingAsync(runId, change);

    /// <summary>Asserts the persisted closure through a fresh context: coherent through the one shared reader and the status query, the
    /// preserved current stage, a cleared participant, exactly one Human event, and the frozen clock.</summary>
    private async Task AssertCoherentClosureAsync(AbandonmentScene scene, RunStage expectedStage, double expectedAccumulatedSeconds)
    {
        await using var verify = fixture.CreateContext();
        var run = await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(RunLifecycle.Abandoned, run.Lifecycle);
        Assert.Equal(expectedStage, run.Stage);
        Assert.Equal(ParticipantIdentity.None(), run.ActiveParticipant);
        Assert.Equal(expectedAccumulatedSeconds, run.AccumulatedAutonomousSeconds);
        Assert.Equal(Now, run.LastAdvancedAtUtc);
        Assert.Equal(Now, run.AbandonedAtUtc);
        Assert.Equal(AbandonmentRaceSupport.Reason, run.AbandonmentReason);
        Assert.Equal(
            1,
            await verify.Events.AsNoTracking().CountAsync(candidate => candidate.RunId == scene.RunId && candidate.EventType == RunEventType.RunAbandoned));

        var reading = await RunAbandonmentReader.ReadAsync(verify, scene.RunId, CancellationToken.None);
        Assert.True(reading is { Coherent: true }, "the persisted closure must be coherent");

        var status = await new GetManualRunAbandonmentQueryHandler(verify)
            .HandleAsync(new GetManualRunAbandonmentQuery(scene.RunId), CancellationToken.None);
        Assert.True(status.IsSuccess);
        Assert.NotNull(status.Value.Abandonment);
        Assert.Equal(AbandonmentRaceSupport.Reason, status.Value.Abandonment.Reason);
    }

    private async Task AssertNormalIntakeAsync(AbandonmentScene scene)
    {
        await using var db = fixture.CreateContext();
        var next = await new CreateManualRunCommandHandler(db, new FixedTimeProvider(Now.AddMinutes(1)))
            .HandleAsync(new CreateManualRunCommand(scene.ProjectId, "Another objective", null, null), CancellationToken.None);
        Assert.True(next.IsSuccess, next.IsFailure ? Code(next) : null);
        Assert.NotEqual(scene.RunId, next.Value.RunId);
        Assert.Equal(2, next.Value.ExecutionNumber);
    }

    [Fact]
    public async Task A_stale_tracked_none_participant_never_hides_a_newer_committed_agent_participant()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        await SeedAsync(scene.RunId, run => run.AdvanceStage(RunStage.Plan, ParticipantIdentity.None(), Created.AddSeconds(20)));
        await using var held = fixture.CreateContext();
        var stale = await held.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(RunStage.Plan, stale.Stage);
        Assert.Equal(ParticipantKind.None, stale.ActiveParticipantKind);
        await CommitCompetingAsync(
            scene.RunId, run => run.AdvanceStage(RunStage.Critique, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), Created.AddSeconds(40)));

        var result = await AbandonAsync(held, scene.RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        // 10s Running before the first advance, 20s to the second, and 60s up to the abandonment.
        await AssertCoherentClosureAsync(scene, RunStage.Critique, expectedAccumulatedSeconds: 90);
        await AssertNormalIntakeAsync(scene);
    }

    [Fact]
    public async Task A_stale_tracked_created_run_that_another_request_claimed_is_closed_as_running_with_its_time_counted()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture);
        await using var held = fixture.CreateContext();
        var stale = await held.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(RunLifecycle.Created, stale.Lifecycle);
        await CommitCompetingAsync(scene.RunId, run => run.Claim(Created.AddSeconds(10)));

        var result = await AbandonAsync(held, scene.RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        // Created time is never counted, but the 90 seconds of the run that was Running when it was abandoned are.
        await AssertCoherentClosureAsync(scene, RunStage.Intake, expectedAccumulatedSeconds: 90);
        await AssertNormalIntakeAsync(scene);
    }

    public static TheoryData<string> CompetingParticipants() => ["Agent with a role", "Agent without a role", "Orchestrator", "Human", "None"];

    private static ParticipantIdentity Participant(string name) => name switch
    {
        "Agent with a role" => ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
        "Agent without a role" => ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
        "Orchestrator" => ParticipantIdentity.ForOrchestrator(),
        "Human" => ParticipantIdentity.ForHuman(),
        _ => ParticipantIdentity.None(),
    };

    [Theory]
    [MemberData(nameof(CompetingParticipants))]
    public async Task A_stale_tracked_running_run_with_no_participant_is_closed_whatever_participant_another_request_committed(string competing)
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        await SeedAsync(scene.RunId, run => run.AdvanceStage(RunStage.Plan, ParticipantIdentity.None(), Created.AddSeconds(20)));
        await using var held = fixture.CreateContext();
        await held.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        await CommitCompetingAsync(scene.RunId, run => run.AdvanceStage(RunStage.Execute, Participant(competing), Created.AddSeconds(55)));

        var result = await AbandonAsync(held, scene.RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await AssertCoherentClosureAsync(scene, RunStage.Execute, expectedAccumulatedSeconds: 90);
    }

    [Theory]
    [MemberData(nameof(CompetingParticipants))]
    public async Task A_stale_tracked_participant_is_replaced_by_none_whatever_another_request_committed(string competing)
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        await SeedAsync(scene.RunId, run => run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), Created.AddSeconds(20)));
        await using var held = fixture.CreateContext();
        await held.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        await CommitCompetingAsync(scene.RunId, run => run.AdvanceStage(RunStage.Critique, Participant(competing), Created.AddSeconds(55)));

        var result = await AbandonAsync(held, scene.RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await AssertCoherentClosureAsync(scene, RunStage.Critique, expectedAccumulatedSeconds: 90);
    }

    [Fact]
    public async Task A_stale_tracked_run_that_advanced_through_several_stages_keeps_the_newest_stage()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        await using var held = fixture.CreateContext();
        await held.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        await CommitCompetingAsync(scene.RunId, run => run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), Created.AddSeconds(25)));
        await CommitCompetingAsync(scene.RunId, run => run.AdvanceStage(RunStage.Execute, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), Created.AddSeconds(70)));

        var result = await AbandonAsync(held, scene.RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await AssertCoherentClosureAsync(scene, RunStage.Execute, expectedAccumulatedSeconds: 90);
        await AssertNormalIntakeAsync(scene);
    }

    [Fact]
    public async Task A_populated_tracker_holding_the_current_run_closes_it_exactly_as_an_empty_tracker_does()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        await SeedAsync(scene.RunId, run => run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), Created.AddSeconds(40)));
        await using var held = fixture.CreateContext();
        var current = await held.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(ParticipantKind.Agent, current.ActiveParticipantKind);

        var result = await AbandonAsync(held, scene.RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await AssertCoherentClosureAsync(scene, RunStage.Plan, expectedAccumulatedSeconds: 90);
        await AssertNormalIntakeAsync(scene);
    }

    [Fact]
    public async Task A_stale_tracked_running_run_that_another_request_ended_is_refused_and_nothing_is_written()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        await using var held = fixture.CreateContext();
        await held.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        await CommitCompetingAsync(scene.RunId, run => run.Complete(Created.AddSeconds(50)));
        var before = await scene.SnapshotAsync();

        var result = await AbandonAsync(held, scene.RunId);

        Assert.Equal(RunAbandonmentErrors.RunNotAbandonableCode, Code(result));
        Assert.Equal(before, await scene.SnapshotAsync());
        Assert.Equal(nameof(RunLifecycle.Completed), (await scene.SnapshotAsync()).Lifecycle);
    }

    [Fact]
    public async Task A_stale_tracked_run_that_another_request_already_abandoned_replays_that_request_without_a_second_event()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        await using var held = fixture.CreateContext();
        await held.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        var first = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, nowUtc: Created.AddSeconds(50));
        Assert.True(first.IsSuccess);

        var replay = await AbandonAsync(held, scene.RunId);

        Assert.True(replay.IsSuccess, replay.IsFailure ? Code(replay) : null);
        Assert.Equal(Created.AddSeconds(50), replay.Value.AbandonedAtUtc);
        Assert.Equal(1, (await scene.SnapshotAsync()).AbandonedEvents);
    }
}
