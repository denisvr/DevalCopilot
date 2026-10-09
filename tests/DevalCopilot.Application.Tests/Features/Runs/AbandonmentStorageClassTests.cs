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

/// <summary>ADR-0031 on real file-backed SQLite: the recorded abandonment time is a fact only when SQLite stores it as TEXT in the written
/// form. A BLOB that holds the UTF-8 bytes of the exact, matching timestamp is a damaged row, not a recorded abandonment: it is
/// incoherent for that one run (no GET, replay or conflict answer, no intake), its bytes and storage class are never rewritten by a
/// refused request or an unrelated save, and no whole-row read, healthy sibling or project list is affected. Each BLOB case is paired
/// with the identical TEXT as a control that stays coherent.</summary>
public sealed class AbandonmentStorageClassTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset ReadAt = AbandonmentScene.Created.AddDays(2);

    private static readonly DateTimeOffset Whole = AbandonmentRaceSupport.AbandonedAt;

    public static TheoryData<string> Instants() => ["whole seconds", "fractional seconds", "positive offset", "negative fractional offset"];

    private static DateTimeOffset Instant(string name) => name switch
    {
        "whole seconds" => Whole,
        "fractional seconds" => Whole.AddTicks(1234567),
        "positive offset" => new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.FromHours(3)),
        "negative fractional offset" => new DateTimeOffset(2026, 10, 8, 6, 30, 0, 250, TimeSpan.FromHours(-5.5)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>A coherently abandoned run at <paramref name="instant"/>, the stored time then turned into a BLOB of the same UTF-8 bytes.</summary>
    private async Task<AbandonmentScene> BlobSceneAsync(string instant, string reason = AbandonmentRaceSupport.Reason)
    {
        var scene = await TextSceneAsync(instant, reason);
        await scene.SqlAsync("UPDATE runs SET AbandonedAtUtc = CAST(AbandonedAtUtc AS BLOB) WHERE Id = {0}", scene.RunId);
        Assert.Equal("blob", (await StoredAsync(scene.RunId)).Class);
        return scene;
    }

    private async Task<AbandonmentScene> TextSceneAsync(string instant, string reason = AbandonmentRaceSupport.Reason)
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        var closed = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, reason, Instant(instant));
        Assert.True(closed.IsSuccess, closed.IsFailure ? Assert.Single(closed.Errors).Code : null);
        Assert.Equal("text", (await StoredAsync(scene.RunId)).Class);
        return scene;
    }

    private async Task<(string Class, string? Hex)> StoredAsync(Guid runId)
    {
        await using var db = fixture.CreateContext();
        var storageClass = await db.Database.SqlQuery<string>($"SELECT typeof(AbandonedAtUtc) AS Value FROM runs WHERE Id = {runId}").SingleAsync();
        var hex = await db.Database.SqlQuery<string>($"SELECT hex(AbandonedAtUtc) AS Value FROM runs WHERE Id = {runId}").SingleOrDefaultAsync();
        return (storageClass, hex);
    }

    private async Task<AbandonmentScene> HealthySiblingAsync()
    {
        var sibling = await AbandonmentScene.CreateAsync(fixture, running: true);
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, sibling.RunId, "A healthy sibling's reason.")).IsSuccess);
        return sibling;
    }

    private async Task<GetManualRunAbandonmentQueryResult> StatusAsync(Guid runId)
    {
        await using var db = fixture.CreateContext();
        var result = await new GetManualRunAbandonmentQueryHandler(db)
            .HandleAsync(new GetManualRunAbandonmentQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    // ---- the identical TEXT is the control ---------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Instants))]
    public async Task The_identical_text_stays_a_coherent_recorded_abandonment_that_replays_and_blocks_nothing_it_should_not(string instant)
    {
        var scene = await TextSceneAsync(instant);
        await using var db = fixture.CreateContext();

        var reading = await RunAbandonmentReader.ReadAsync(db, scene.RunId, CancellationToken.None);
        var status = await StatusAsync(scene.RunId);
        var replay = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, nowUtc: ReadAt);
        var conflict = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, "A different reason.", ReadAt);
        var intake = await new CreateManualRunCommandHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new CreateManualRunCommand(scene.ProjectId, "Another objective", null, null), CancellationToken.None);

        Assert.True(reading!.Coherent);
        Assert.Equal(Instant(instant), reading.AbandonedAtUtc);
        Assert.Equal(RunAbandonmentErrors.AlreadyAbandonedCode, status.RefusalCode);
        Assert.Equal(AbandonmentRaceSupport.Reason, status.Abandonment!.Reason);
        Assert.Equal(Instant(instant), status.Abandonment.AbandonedAtUtc);
        Assert.True(replay.IsSuccess);
        Assert.Equal(Instant(instant), replay.Value.AbandonedAtUtc);
        Assert.Equal(RunAbandonmentErrors.ReasonConflictCode, Assert.Single(conflict.Errors).Code);
        Assert.True(intake.IsSuccess, intake.IsFailure ? Assert.Single(intake.Errors).Code : null);
    }

    // ---- a BLOB of the same bytes is a damaged row -----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Instants))]
    public async Task A_blob_of_the_matching_timestamp_reads_as_no_time_and_is_incoherent_for_the_reader_and_the_status(string instant)
    {
        var scene = await BlobSceneAsync(instant);
        var stored = await StoredAsync(scene.RunId);
        await using var db = fixture.CreateContext();

        var reading = await RunAbandonmentReader.ReadAsync(db, scene.RunId, CancellationToken.None);
        var status = await StatusAsync(scene.RunId);

        Assert.False(reading!.Coherent);
        Assert.Null(reading.AbandonedAtUtc);
        Assert.False(status.Eligible);
        Assert.Equal(RunAbandonmentErrors.AbandonmentIncoherentCode, status.RefusalCode);
        Assert.Null(status.Abandonment);
        Assert.Equal(stored, await StoredAsync(scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Instants))]
    public async Task A_same_or_different_reason_never_replays_or_conflicts_against_a_blob_and_nothing_is_written(string instant)
    {
        var scene = await BlobSceneAsync(instant);
        var before = await scene.SnapshotAsync();
        var stored = await StoredAsync(scene.RunId);
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
        Assert.Equal(stored, await StoredAsync(scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Instants))]
    public async Task Another_objective_stays_blocked_and_nothing_is_created_or_rewritten_with_a_populated_tracker(string instant)
    {
        var scene = await BlobSceneAsync(instant);
        var before = await scene.SnapshotAsync();
        var stored = await StoredAsync(scene.RunId);
        await using var populated = fixture.CreateContext();
        await populated.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);

        var result = await new CreateManualRunCommandHandler(populated, new FixedTimeProvider(ReadAt))
            .HandleAsync(new CreateManualRunCommand(scene.ProjectId, "Another objective", null, null), CancellationToken.None);

        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(before, await scene.SnapshotAsync());
        Assert.Equal(stored, await StoredAsync(scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Instants))]
    public async Task The_whole_row_still_loads_tracked_and_untracked_and_an_unrelated_save_leaves_the_blob_untouched(string instant)
    {
        var scene = await BlobSceneAsync(instant);
        var stored = await StoredAsync(scene.RunId);
        await using var db = fixture.CreateContext();

        var cockpit = await new GetRunCockpitQueryHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new GetRunCockpitQuery(scene.RunId), CancellationToken.None);
        var untracked = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.RunId);
        var tracked = await db.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        db.Entry(tracked).Property(candidate => candidate.AccumulatedAutonomousSeconds).CurrentValue += 1;
        await db.SaveChangesAsync();

        Assert.True(cockpit.IsSuccess, cockpit.IsFailure ? Assert.Single(cockpit.Errors).Code : null);
        Assert.Equal(RunLifecycle.Abandoned, cockpit.Value.Lifecycle);
        Assert.Null(untracked.AbandonedAtUtc);
        Assert.Null(tracked.AbandonedAtUtc);
        Assert.Equal(stored, await StoredAsync(scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Instants))]
    public async Task A_healthy_sibling_and_the_project_list_are_unaffected_by_the_blob(string instant)
    {
        var damaged = await BlobSceneAsync(instant);
        var sibling = await HealthySiblingAsync();
        var control = await TextSceneAsync(instant, "A text control's reason.");
        await using var db = fixture.CreateContext();

        var all = await RunAbandonmentReader.ReadAllAsync(db, CancellationToken.None);
        var summaries = await new GetProjectRunSummariesQueryHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new GetProjectRunSummariesQuery(), CancellationToken.None);

        Assert.False(all.Single(reading => reading.RunId == damaged.RunId).Coherent);
        Assert.True(all.Single(reading => reading.RunId == sibling.RunId).Coherent);
        Assert.True(all.Single(reading => reading.RunId == control.RunId).Coherent);
        Assert.False(summaries.Single(summary => summary.ProjectId == damaged.ProjectId).CanCreateRun);
        Assert.True(summaries.Single(summary => summary.ProjectId == sibling.ProjectId).CanCreateRun);
        Assert.True(summaries.Single(summary => summary.ProjectId == control.ProjectId).CanCreateRun);
        var next = await new CreateManualRunCommandHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new CreateManualRunCommand(sibling.ProjectId, "The sibling's next objective", null, null), CancellationToken.None);
        Assert.True(next.IsSuccess);
    }

    // ---- every other storage class is a damaged row too ---------------------------------------------------------------------------

    public static TheoryData<string> OtherClasses() => ["null", "integer", "real", "blob of unrelated bytes", "empty blob", "blob of the text with a trailing NUL"];

    [Theory]
    [MemberData(nameof(OtherClasses))]
    public async Task Any_other_storage_class_is_incoherent_untouched_and_isolated(string name)
    {
        var scene = await TextSceneAsync("whole seconds");
        var sql = name switch
        {
            "null" => "UPDATE runs SET AbandonedAtUtc = NULL WHERE Id = {0}",
            "integer" => "UPDATE runs SET AbandonedAtUtc = 5 WHERE Id = {0}",
            "real" => "UPDATE runs SET AbandonedAtUtc = 2460000.5 WHERE Id = {0}",
            "blob of unrelated bytes" => "UPDATE runs SET AbandonedAtUtc = X'010203' WHERE Id = {0}",
            "empty blob" => "UPDATE runs SET AbandonedAtUtc = X'' WHERE Id = {0}",
            _ => "UPDATE runs SET AbandonedAtUtc = CAST(AbandonedAtUtc AS BLOB) || X'00' WHERE Id = {0}",
        };
        await scene.SqlAsync(sql, scene.RunId);
        var before = await scene.SnapshotAsync();
        var stored = await StoredAsync(scene.RunId);
        var sibling = await HealthySiblingAsync();
        await using var db = fixture.CreateContext();

        var status = await StatusAsync(scene.RunId);
        var replay = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, nowUtc: ReadAt);
        var cockpit = await new GetRunCockpitQueryHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new GetRunCockpitQuery(scene.RunId), CancellationToken.None);
        var all = await RunAbandonmentReader.ReadAllAsync(db, CancellationToken.None);

        Assert.Equal(RunAbandonmentErrors.AbandonmentIncoherentCode, status.RefusalCode);
        Assert.Null(status.Abandonment);
        Assert.Equal(RunAbandonmentErrors.AbandonmentIncoherentCode, Assert.Single(replay.Errors).Code);
        Assert.True(cockpit.IsSuccess);
        Assert.False(all.Single(reading => reading.RunId == scene.RunId).Coherent);
        Assert.True(all.Single(reading => reading.RunId == sibling.RunId).Coherent);
        Assert.Equal(before, await scene.SnapshotAsync());
        Assert.Equal(stored, await StoredAsync(scene.RunId));
    }
}
