using DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetManualRunAbandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: what the read side says about an abandonment. The status query is advisory and strictly read-only, the project
/// summary offers another objective only through the coherent facts intake itself uses, and a cockpit read never adds time to an
/// abandoned run.</summary>
public sealed class AbandonmentReadModelTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset ReadAt = AbandonmentScene.Created.AddDays(2);

    private async Task<GetManualRunAbandonmentQueryResult> StatusAsync(Guid runId)
    {
        await using var db = fixture.CreateContext();
        var result = await new GetManualRunAbandonmentQueryHandler(db)
            .HandleAsync(new GetManualRunAbandonmentQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null);
        return result.Value;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_created_or_running_manual_run_with_nothing_active_is_advisorily_eligible(bool running)
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running, withWorkspace: true);
        var before = await scene.SnapshotAsync();

        var status = await StatusAsync(scene.RunId);

        Assert.True(status.Eligible);
        Assert.Null(status.RefusalCode);
        Assert.Null(status.Abandonment);
        Assert.True(status.LatestEventSequence > 0);
        Assert.Equal(before, await scene.SnapshotAsync());
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        await using var db = fixture.CreateContext();

        var result = await new GetManualRunAbandonmentQueryHandler(db)
            .HandleAsync(new GetManualRunAbandonmentQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(RunAbandonmentErrors.RunNotFoundCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task Every_blocker_is_reported_as_a_fixed_code_and_nothing_is_written()
    {
        var running = await AbandonmentScene.CreateAsync(fixture, running: true, withWorkspace: true);
        await running.AddAttemptAsync();
        var verification = await AbandonmentScene.CreateAsync(fixture, running: true, withWorkspace: true);
        await verification.AddVerificationAsync();
        var commit = await AbandonmentScene.CreateAsync(fixture, running: true, withWorkspace: true);
        await commit.AddLocalCommitOperationAsync(LocalCommitStatus.NeedsAttention);
        var workspace = await AbandonmentScene.CreateAsync(fixture, running: true, withWorkspace: true);
        await workspace.SqlAsync("UPDATE git_workspaces SET Status = 'Committing' WHERE Id = {0}", workspace.WorkspaceId!);
        var legacy = await AbandonmentScene.CreateAsync(fixture, mode: RunExecutionMode.Legacy);
        var completed = await AbandonmentScene.CreateAsync(fixture, running: true);
        await completed.SqlAsync("UPDATE runs SET Lifecycle = 'Completed' WHERE Id = {0}", completed.RunId);

        var cases = new (AbandonmentScene Scene, string Code)[]
        {
            (running, RunAbandonmentErrors.ActiveAttemptCode),
            (verification, RunAbandonmentErrors.ActiveVerificationCode),
            (commit, RunAbandonmentErrors.LocalCommitOpenCode),
            (workspace, RunAbandonmentErrors.WorkspaceBusyCode),
            (legacy, RunAbandonmentErrors.RunNotManualCode),
            (completed, RunAbandonmentErrors.RunNotAbandonableCode),
        };
        foreach (var (scene, code) in cases)
        {
            var before = await scene.SnapshotAsync();
            var status = await StatusAsync(scene.RunId);

            Assert.False(status.Eligible, code);
            Assert.Equal(code, status.RefusalCode);
            Assert.Null(status.Abandonment);
            Assert.Equal(before, await scene.SnapshotAsync());
        }
    }

    [Fact]
    public async Task A_coherently_abandoned_run_reports_its_recorded_reason_and_time_and_is_no_longer_eligible()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId)).IsSuccess);

        var status = await StatusAsync(scene.RunId);

        Assert.False(status.Eligible);
        Assert.Equal(RunAbandonmentErrors.AlreadyAbandonedCode, status.RefusalCode);
        Assert.NotNull(status.Abandonment);
        Assert.Equal(AbandonmentRaceSupport.Reason, status.Abandonment.Reason);
        Assert.Equal(AbandonmentRaceSupport.AbandonedAt, status.Abandonment.AbandonedAtUtc);
    }

    [Theory]
    [InlineData("no-reason")]
    [InlineData("no-event")]
    [InlineData("legacy-mode")]
    public async Task An_incoherent_abandoned_run_reports_no_abandonment_and_a_fixed_incoherence_code(string breakage)
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId)).IsSuccess);
        switch (breakage)
        {
            case "no-reason": await scene.SqlAsync("UPDATE runs SET AbandonmentReason = NULL WHERE Id = {0}", scene.RunId); break;
            case "no-event": await scene.SqlAsync("DELETE FROM events WHERE EventType = 'run.abandoned' AND RunId = {0}", scene.RunId); break;
            default: await scene.SetRunModeAsync("0"); break;
        }

        var status = await StatusAsync(scene.RunId);

        Assert.False(status.Eligible);
        Assert.Null(status.Abandonment);
        Assert.Equal(
            breakage == "legacy-mode" ? RunAbandonmentErrors.RunNotManualCode : RunAbandonmentErrors.AbandonmentIncoherentCode,
            status.RefusalCode);
    }

    // ---- the project summary -------------------------------------------------------------------------------------------------

    private async Task<ProjectRunSummaryQueryResult> SummaryAsync(Guid projectId)
    {
        await using var db = fixture.CreateContext();
        var all = await new GetProjectRunSummariesQueryHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new GetProjectRunSummariesQuery(), CancellationToken.None);
        return all.Single(summary => summary.ProjectId == projectId);
    }

    [Fact]
    public async Task The_project_summary_shows_the_abandoned_lifecycle_and_offers_another_objective_only_for_coherent_facts()
    {
        var coherent = await AbandonmentScene.CreateAsync(fixture, running: true);
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, coherent.RunId)).IsSuccess);
        var incoherent = await AbandonmentScene.CreateAsync(fixture, running: true);
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, incoherent.RunId)).IsSuccess);
        await incoherent.SqlAsync("UPDATE runs SET AbandonmentReason = NULL WHERE Id = {0}", incoherent.RunId);

        var offered = await SummaryAsync(coherent.ProjectId);
        var withheld = await SummaryAsync(incoherent.ProjectId);

        Assert.Equal(RunLifecycle.Abandoned, offered.Lifecycle);
        Assert.Equal(coherent.RunId, offered.RunId);
        Assert.True(offered.CanCreateRun);
        Assert.Equal(RunExecutionMode.ManualAgent, offered.ExecutionMode);
        Assert.Equal(RunLifecycle.Abandoned, withheld.Lifecycle);
        Assert.False(withheld.CanCreateRun);
    }

    [Fact]
    public async Task A_newer_active_run_is_the_projects_summarized_run_even_beside_an_abandoned_one()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId)).IsSuccess);
        await using (var db = fixture.CreateContext())
        {
            var project = await db.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
            db.Runs.Add(Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Newer", ReadAt));
            await db.SaveChangesAsync();
        }

        var summary = await SummaryAsync(scene.ProjectId);

        Assert.Equal(RunLifecycle.Created, summary.Lifecycle);
        Assert.Equal(2, summary.ExecutionNumber);
        Assert.False(summary.CanCreateRun);
    }

    // ---- the frozen clock ----------------------------------------------------------------------------------------------------

    private async Task<GetRunCockpitQueryResult> CockpitAsync(Guid runId, DateTimeOffset now)
    {
        await using var db = fixture.CreateContext();
        var result = await new GetRunCockpitQueryHandler(db, new FixedTimeProvider(now))
            .HandleAsync(new GetRunCockpitQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public async Task A_running_clock_ticks_but_an_abandoned_runs_cockpit_time_is_frozen_for_every_later_read()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        var ticking = await CockpitAsync(scene.RunId, AbandonmentScene.Created.AddSeconds(70));
        Assert.Equal(60, ticking.AutonomousDurationSeconds);
        Assert.Equal("Running", ticking.Lifecycle.ToString());

        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, nowUtc: AbandonmentScene.Created.AddSeconds(100))).IsSuccess);

        var atAbandonment = await CockpitAsync(scene.RunId, AbandonmentScene.Created.AddSeconds(100));
        var muchLater = await CockpitAsync(scene.RunId, AbandonmentScene.Created.AddDays(40));
        Assert.Equal(90, atAbandonment.AutonomousDurationSeconds);
        Assert.Equal(90, muchLater.AutonomousDurationSeconds);
        Assert.Equal("Abandoned", muchLater.Lifecycle.ToString());
        Assert.Equal(RunStage.Intake, muchLater.Stage);
    }

    [Fact]
    public async Task A_created_runs_abandonment_adds_no_time_to_the_cockpit()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture);
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, nowUtc: AbandonmentScene.Created.AddHours(9))).IsSuccess);

        var cockpit = await CockpitAsync(scene.RunId, AbandonmentScene.Created.AddDays(3));

        Assert.Equal(0, cockpit.AutonomousDurationSeconds);
    }
}
