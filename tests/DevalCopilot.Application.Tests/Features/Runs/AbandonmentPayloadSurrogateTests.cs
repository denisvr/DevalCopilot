using DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetManualRunAbandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031 on real file-backed SQLite: an abandonment event payload whose reason string holds an escaped surrogate that is not
/// part of a valid pair is valid JSON but no text, so it is no reason and keeps that one run's abandonment incoherent. It never throws
/// from any shared reading (status, replay arbitration, intake, project summaries, recorded history inspection), cannot answer a replay
/// or a conflict, cannot permit another objective, never disturbs a healthy run or project, and is never repaired by a refusal or a
/// read. The valid supplementary characters a producer writes stay coherent in every form.</summary>
public sealed class AbandonmentPayloadSurrogateTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private const char Backslash = (char)92;

    private static readonly DateTimeOffset ReadAt = AbandonmentScene.Created.AddDays(2);

    public static TheoryData<string> Damages() =>
    [
        "isolated high surrogate",
        "isolated low surrogate",
        "misordered pair",
    ];

    private static string Escape(string hex) => Backslash + "u" + hex;

    private static string Payload(string escapedText) => "{\"reason\":\"" + escapedText + "\"}";

    private static string DamagedPayload(string name) => Payload(name switch
    {
        "isolated high surrogate" => Escape("d800"),
        "isolated low surrogate" => Escape("dc00"),
        "misordered pair" => Escape("dd1e") + Escape("d834"),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    });

    private async Task WritePayloadAsync(AbandonmentScene scene, string payload) => await scene.SqlAsync(
        "UPDATE events SET PayloadJson = {0} WHERE RunId = {1} AND EventType = 'run.abandoned'", payload, scene.RunId);

    private async Task<string?> RawPayloadAsync(Guid runId)
    {
        await using var db = fixture.CreateContext();
        return await db.Database.SqlQuery<string>(
            $"SELECT hex(PayloadJson) AS Value FROM events WHERE RunId = {runId} AND EventType = 'run.abandoned'").SingleOrDefaultAsync();
    }

    /// <summary>A coherently abandoned manual run whose event payload another writer then damaged.</summary>
    private async Task<AbandonmentScene> DamagedSceneAsync(string name, bool running = true)
    {
        var scene = await AbandonmentScene.CreateAsync(fixture, running);
        var closed = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId);
        Assert.True(closed.IsSuccess, closed.IsFailure ? Assert.Single(closed.Errors).Code : null);
        await WritePayloadAsync(scene, DamagedPayload(name));
        return scene;
    }

    private async Task<AbandonmentScene> HealthyNeighbourAsync()
    {
        var neighbour = await AbandonmentScene.CreateAsync(fixture, running: true);
        var closed = await AbandonmentRaceSupport.AbandonAsync(fixture, neighbour.RunId, "A healthy neighbour's reason.");
        Assert.True(closed.IsSuccess, closed.IsFailure ? Assert.Single(closed.Errors).Code : null);
        return neighbour;
    }

    private async Task<GetRunAbandonmentInspectionQueryResult> InspectAsync(Guid runId)
    {
        await using var db = fixture.CreateContext();
        var result = await new GetRunAbandonmentInspectionQueryHandler(db)
            .HandleAsync(new GetRunAbandonmentInspectionQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null);
        return result.Value;
    }

    private async Task<GetManualRunAbandonmentQueryResult> StatusAsync(Guid runId)
    {
        await using var db = fixture.CreateContext();
        var result = await new GetManualRunAbandonmentQueryHandler(db)
            .HandleAsync(new GetManualRunAbandonmentQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null);
        return result.Value;
    }

    [Theory]
    [MemberData(nameof(Damages))]
    public async Task The_reading_the_status_and_the_inspection_report_it_incoherent_and_never_throw(string name)
    {
        var scene = await DamagedSceneAsync(name);
        var before = await scene.SnapshotAsync();
        var raw = await RawPayloadAsync(scene.RunId);
        await using var db = fixture.CreateContext();

        var reading = await RunAbandonmentReader.ReadAsync(db, scene.RunId, CancellationToken.None);
        var status = await StatusAsync(scene.RunId);
        var inspection = await InspectAsync(scene.RunId);

        Assert.NotNull(reading);
        Assert.False(reading.Coherent);
        Assert.Equal(AbandonmentRaceSupport.Reason, reading.Reason);
        Assert.Equal(AbandonmentRaceSupport.AbandonedAt, reading.AbandonedAtUtc);
        Assert.False(status.Eligible);
        Assert.Equal(RunAbandonmentErrors.AbandonmentIncoherentCode, status.RefusalCode);
        Assert.Null(status.Abandonment);
        Assert.Equal(RunAbandonmentInspectionStatus.Unavailable, inspection.Status);
        Assert.Null(inspection.Abandonment);
        Assert.Equal(before, await scene.SnapshotAsync());
        Assert.Equal(raw, await RawPayloadAsync(scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Damages))]
    public async Task A_replay_and_a_conflict_are_both_refused_as_incoherent_and_nothing_is_written(string name)
    {
        var scene = await DamagedSceneAsync(name);
        var before = await scene.SnapshotAsync();
        var raw = await RawPayloadAsync(scene.RunId);

        // A populated tracker: the context already tracks the project and a healthy neighbour run when the request arrives.
        var neighbour = await HealthyNeighbourAsync();
        await using var populated = fixture.CreateContext();
        await populated.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);
        await populated.Runs.SingleAsync(candidate => candidate.Id == neighbour.RunId);

        foreach (var reason in new[] { AbandonmentRaceSupport.Reason, "A different reason." })
        {
            var result = await new AbandonManualRunCommandHandler(populated, new FixedTimeProvider(ReadAt))
                .HandleAsync(new AbandonManualRunCommand(scene.RunId, reason), CancellationToken.None);

            Assert.Equal(RunAbandonmentErrors.AbandonmentIncoherentCode, Assert.Single(result.Errors).Code);
        }

        Assert.Equal(before, await scene.SnapshotAsync());
        Assert.Equal(raw, await RawPayloadAsync(scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Damages))]
    public async Task Another_objective_is_still_blocked_and_nothing_is_created_even_with_a_populated_tracker(string name)
    {
        var scene = await DamagedSceneAsync(name);
        var before = await scene.SnapshotAsync();
        var raw = await RawPayloadAsync(scene.RunId);
        await using var populated = fixture.CreateContext();
        await populated.Projects.SingleAsync(candidate => candidate.Id == scene.ProjectId);

        var result = await new CreateManualRunCommandHandler(populated, new FixedTimeProvider(ReadAt))
            .HandleAsync(new CreateManualRunCommand(scene.ProjectId, "Another objective", null, null), CancellationToken.None);

        Assert.Equal(RunIntentRecorder.BlockedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(before, await scene.SnapshotAsync());
        Assert.Equal(raw, await RawPayloadAsync(scene.RunId));
    }

    [Theory]
    [MemberData(nameof(Damages))]
    public async Task A_healthy_neighbour_and_the_project_list_are_unaffected_and_the_damaged_project_stays_blocked(string name)
    {
        var damaged = await DamagedSceneAsync(name);
        var neighbour = await HealthyNeighbourAsync();
        await using var db = fixture.CreateContext();

        var all = await RunAbandonmentReader.ReadAllAsync(db, CancellationToken.None);
        var summaries = await new GetProjectRunSummariesQueryHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new GetProjectRunSummariesQuery(), CancellationToken.None);

        Assert.False(all.Single(reading => reading.RunId == damaged.RunId).Coherent);
        var healthy = all.Single(reading => reading.RunId == neighbour.RunId);
        Assert.True(healthy.Coherent);
        Assert.Equal(AbandonmentRaceSupport.AbandonedAt, healthy.AbandonedAtUtc);
        Assert.False(summaries.Single(summary => summary.ProjectId == damaged.ProjectId).CanCreateRun);
        Assert.True(summaries.Single(summary => summary.ProjectId == neighbour.ProjectId).CanCreateRun);

        var next = await new CreateManualRunCommandHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new CreateManualRunCommand(neighbour.ProjectId, "The neighbour's next objective", null, null), CancellationToken.None);
        Assert.True(next.IsSuccess, next.IsFailure ? Assert.Single(next.Errors).Code : null);

        var neighbourStatus = await StatusAsync(neighbour.RunId);
        Assert.Equal("A healthy neighbour's reason.", neighbourStatus.Abandonment!.Reason);
        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, (await InspectAsync(neighbour.RunId)).Status);
    }

    [Theory]
    [MemberData(nameof(Damages))]
    public async Task A_healthy_earlier_run_of_the_same_project_is_still_read_beside_the_damaged_one(string name)
    {
        var earlier = await AbandonmentScene.CreateAsync(fixture, running: true);
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, earlier.RunId, "An earlier reason.")).IsSuccess);
        await using var setup = fixture.CreateContext();
        var created = await new CreateManualRunCommandHandler(setup, new FixedTimeProvider(ReadAt))
            .HandleAsync(new CreateManualRunCommand(earlier.ProjectId, "The next objective", null, null), CancellationToken.None);
        Assert.True(created.IsSuccess, created.IsFailure ? Assert.Single(created.Errors).Code : null);
        var laterRunId = created.Value.RunId;
        Assert.True((await AbandonmentRaceSupport.AbandonAsync(fixture, laterRunId, nowUtc: ReadAt)).IsSuccess);
        await earlier.SqlAsync(
            "UPDATE events SET PayloadJson = {0} WHERE RunId = {1} AND EventType = 'run.abandoned'", DamagedPayload(name), laterRunId);
        await using var db = fixture.CreateContext();

        var readings = await RunAbandonmentReader.ReadForProjectAsync(db, earlier.ProjectId, CancellationToken.None);

        Assert.Equal(2, readings.Count);
        Assert.True(readings.Single(reading => reading.RunId == earlier.RunId).Coherent);
        Assert.False(readings.Single(reading => reading.RunId == laterRunId).Coherent);
        Assert.Equal("An earlier reason.", (await StatusAsync(earlier.RunId)).Abandonment!.Reason);
    }

    // ---- the valid supplementary characters a producer writes are the control ----------------------------------------------------

    public static TheoryData<string> ValidForms() =>
    [
        "as written by the producer",
        "escaped pair in lower case digits",
        "escaped pair in upper case digits",
        "literal supplementary character",
    ];

    [Theory]
    [MemberData(nameof(ValidForms))]
    public async Task A_valid_supplementary_reason_stays_coherent_in_every_form_and_replays_exactly(string form)
    {
        var reason = "Replaced by a " + char.ConvertFromUtf32(0x1D11E) + " plan " + char.ConvertFromUtf32(0x1F600) + ".";
        var scene = await AbandonmentScene.CreateAsync(fixture, running: true);
        var closed = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, reason);
        Assert.True(closed.IsSuccess, closed.IsFailure ? Assert.Single(closed.Errors).Code : null);
        var rewritten = form switch
        {
            "as written by the producer" => null,
            "escaped pair in lower case digits" => Payload(
                "Replaced by a " + Escape("d834") + Escape("dd1e") + " plan " + Escape("d83d") + Escape("de00") + "."),
            "escaped pair in upper case digits" => Payload(
                "Replaced by a " + Escape("D834") + Escape("DD1E") + " plan " + Escape("D83D") + Escape("DE00") + "."),
            _ => Payload(reason),
        };
        if (rewritten is not null)
        {
            await WritePayloadAsync(scene, rewritten);
        }

        await using var db = fixture.CreateContext();
        var reading = await RunAbandonmentReader.ReadAsync(db, scene.RunId, CancellationToken.None);
        var status = await StatusAsync(scene.RunId);
        var inspection = await InspectAsync(scene.RunId);
        var replay = await AbandonmentRaceSupport.AbandonAsync(fixture, scene.RunId, reason, ReadAt);
        var intake = await new CreateManualRunCommandHandler(db, new FixedTimeProvider(ReadAt))
            .HandleAsync(new CreateManualRunCommand(scene.ProjectId, "Another objective", null, null), CancellationToken.None);

        Assert.True(reading!.Coherent);
        Assert.Equal(reason, reading.Reason);
        Assert.Equal(RunAbandonmentErrors.AlreadyAbandonedCode, status.RefusalCode);
        Assert.Equal(reason, status.Abandonment!.Reason);
        Assert.Equal(reason, Assert.IsType<RecordedAbandonment>(inspection.Abandonment).Reason);
        Assert.True(replay.IsSuccess, replay.IsFailure ? Assert.Single(replay.Errors).Code : null);
        Assert.True(intake.IsSuccess, intake.IsFailure ? Assert.Single(intake.Errors).Code : null);
    }
}
