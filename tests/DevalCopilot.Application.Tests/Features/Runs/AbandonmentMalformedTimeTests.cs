using DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetManualRunAbandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031 on real file-backed SQLite: a stored abandonment time that cannot be read as a time (a damaged or hand-edited row)
/// keeps that one run's abandonment incoherent. It is never a valid or default time, cannot answer a replay or a conflict, cannot
/// permit another objective, and never makes any read of the run, of its project's neighbours or of the project list fail.</summary>
public sealed class AbandonmentMalformedTimeTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset ReadAt = AbandonmentScene.Created.AddDays(2);

    public static TheoryData<string> Malformed() =>
    [
        "unparsable text",
        "empty text",
        "impossible calendar date",
        "impossible offset",
        "date without an offset",
        "truncated date",
        "padded text",
        "an integer",
        "a real number",
        "a blob",
    ];

    private static object StoredValue(string name) => name switch
    {
        "unparsable text" => "not-a-date",
        "empty text" => string.Empty,
        "impossible calendar date" => "2026-13-45 99:99:99+00:00",
        "impossible offset" => "2026-10-08 12:00:00.0000000+99:99",
        "date without an offset" => "2026-10-08 12:00:00",
        "truncated date" => "2026-10-08",
        "padded text" => " 2026-10-08 12:00:00+00:00 ",
        "an integer" => 5L,
        "a real number" => 2460000.5d,
        "a blob" => new byte[] { 1, 2, 3 },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>A coherently abandoned manual run whose stored abandonment time another writer then damaged.</summary>
    private async Task<AbandonmentScene> MalformedSceneAsync(string name, bool running = true)
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running);
        var closed = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId);
        Assert.True(closed.IsSuccess, closed.IsFailure ? Assert.Single(closed.Errors).Code : null);
        await scene.SqlAsync("UPDATE runs SET AbandonedAtUtc = {0} WHERE Id = {1}", StoredValue(name), scene.RunId);
        return scene;
    }

    private async Task<AbandonmentScene> HealthySiblingAsync()
    {
        var sibling = await AbandonmentScene.CreateAsync(fixture, running: true);
        var closed = await AbandonmentRaceSupport.AbandonAsync(fixture, sibling.RunId, "A healthy sibling's reason.");
        Assert.True(closed.IsSuccess);
        return sibling;
    }

    private async Task<string?> RawStoredTimeAsync(Guid runId)
    {
        await using var db = fixture.CreateContext();
        return await db.Database.SqlQuery<string>($"SELECT CAST(AbandonedAtUtc AS TEXT) AS Value FROM runs WHERE Id = {runId}").SingleOrDefaultAsync();
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task The_status_reports_the_abandonment_as_incoherent_and_never_throws(string name)
    {
        var scene = await MalformedSceneAsync(name);
        var raw = await RawStoredTimeAsync(scene.RunId);
        await using var db = fixture.CreateContext();

        var result = await new GetManualRunAbandonmentQueryHandler(db)
            .HandleAsync(new GetManualRunAbandonmentQuery(scene.RunId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null);
        Assert.False(result.Value.Eligible);
        Assert.Equal(RunAbandonmentErrors.AbandonmentIncoherentCode, result.Value.RefusalCode);
        Assert.Null(result.Value.Abandonment);
        Assert.Equal(raw, await RawStoredTimeAsync(scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task The_reading_is_incoherent_with_no_time_rather_than_a_default_time(string name)
    {
        var scene = await MalformedSceneAsync(name);
        await using var db = fixture.CreateContext();

        var reading = await RunAbandonmentReader.ReadAsync(db, scene.RunId, CancellationToken.None);

        Assert.NotNull(reading);
        Assert.False(reading.Coherent);
        Assert.Null(reading.AbandonedAtUtc);
        Assert.Equal(AbandonmentRaceSupport.Reason, reading.Reason);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task A_replay_and_a_conflict_are_both_refused_as_incoherent_and_nothing_is_written(string name)
    {
        var scene = await MalformedSceneAsync(name);
        var before = await scene.SnapshotAsync();
        var raw = await RawStoredTimeAsync(scene.RunId);

        // A populated tracker: the context already tracks the project and a healthy sibling run when the request arrives.
        var sibling = await HealthySiblingAsync();
        await using var populated = fixture.CreateContext();
        await populated.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
        await populated.Runs.SingleAsync(candidate => candidate.Id == sibling.RunId);

        foreach (var reason in new[] { AbandonmentRaceSupport.Reason, "A different reason." })
        {
            var result = await new AbandonManualRunCommandHandler(populated, new FixedTimeProvider(ReadAt))
                .HandleAsync(new AbandonManualRunCommand(scene.RunId, reason), CancellationToken.None);

            Assert.Equal(RunAbandonmentErrors.AbandonmentIncoherentCode, Assert.Single(result.Errors).Code);
        }

        Assert.Equal(before, await scene.SnapshotAsync());
        Assert.Equal(raw, await RawStoredTimeAsync(scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Another_objective_is_still_blocked_and_nothing_is_created_even_with_a_populated_tracker(string name)
    {
        var scene = await MalformedSceneAsync(name);
        var before = await scene.SnapshotAsync();
        await using var populated = fixture.CreateContext();
        await populated.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);

        var result = await new CreateManualRunCommandHandler(populated, new FixedTimeProvider(ReadAt))
            .HandleAsync(new CreateManualRunCommand(scene.ProjectId, "Another objective", null, null), CancellationToken.None);

        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(before, await scene.SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task A_healthy_sibling_and_the_project_list_are_unaffected_and_the_malformed_project_stays_blocked(string name)
    {
        var malformed = await MalformedSceneAsync(name);
        var sibling = await HealthySiblingAsync();
        await using var db = fixture.CreateContext();

        var all = await RunAbandonmentReader.ReadAllAsync(db, CancellationToken.None);
        var summaries = await new GetProjectRunSummariesQueryHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new GetProjectRunSummariesQuery(), CancellationToken.None);

        Assert.False(all.Single(reading => reading.RunId == malformed.RunId).Coherent);
        var healthy = all.Single(reading => reading.RunId == sibling.RunId);
        Assert.True(healthy.Coherent);
        Assert.Equal(AbandonmentRaceSupport.AbandonedAt, healthy.AbandonedAtUtc);
        Assert.False(summaries.Single(summary => summary.ProjectId == malformed.ProjectId).CanCreateRun);
        Assert.True(summaries.Single(summary => summary.ProjectId == sibling.ProjectId).CanCreateRun);

        var next = await new CreateManualRunCommandHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new CreateManualRunCommand(sibling.ProjectId, "The sibling's next objective", null, null), CancellationToken.None);
        Assert.True(next.IsSuccess, next.IsFailure ? Assert.Single(next.Errors).Code : null);

        var siblingStatus = await new GetManualRunAbandonmentQueryHandler(db)
            .HandleAsync(new GetManualRunAbandonmentQuery(sibling.RunId), CancellationToken.None);
        Assert.Equal("A healthy sibling's reason.", siblingStatus.Value.Abandonment!.Reason);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task The_cockpit_of_the_malformed_run_still_loads_and_adds_no_time(string name)
    {
        var scene = await MalformedSceneAsync(name);
        await using var db = fixture.CreateContext();

        var cockpit = await new GetRunCockpitQueryHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new GetRunCockpitQuery(scene.RunId), CancellationToken.None);

        Assert.True(cockpit.IsSuccess, cockpit.IsFailure ? Assert.Single(cockpit.Errors).Code : null);
        Assert.Equal(RunLifecycle.Abandoned, cockpit.Value.Lifecycle);
    }

    [Fact]
    public async Task A_stored_time_that_is_valid_but_differs_from_the_last_advance_is_incoherent_not_repaired()
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId)).IsSuccess);
        await scene.SqlAsync("UPDATE runs SET AbandonedAtUtc = {0} WHERE Id = {1}", "2026-10-08 12:00:01+00:00", scene.RunId);
        await using var db = fixture.CreateContext();

        var reading = await RunAbandonmentReader.ReadAsync(db, scene.RunId, CancellationToken.None);

        Assert.False(reading!.Coherent);
        Assert.Equal(AbandonmentRaceSupport.AbandonedAt.AddSeconds(1), reading.AbandonedAtUtc);
    }

    [Fact]
    public async Task The_time_the_provider_writes_with_a_whole_second_or_a_fraction_is_read_back_as_coherent()
    {
        foreach (var at in new[] { AbandonmentRaceSupport.AbandonedAt, AbandonmentRaceSupport.AbandonedAt.AddTicks(1234567), AbandonmentRaceSupport.AbandonedAt.AddMilliseconds(500) })
        {
            var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
            Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, nowUtc: at)).IsSuccess);
            await using var db = fixture.CreateContext();

            var reading = await RunAbandonmentReader.ReadAsync(db, scene.RunId, CancellationToken.None);

            Assert.True(reading!.Coherent);
            Assert.Equal(at, reading.AbandonedAtUtc);
        }
    }
}
