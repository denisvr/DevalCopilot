using DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RunAbandonmentInspection;

/// <summary>
/// The three statuses and the fixed 404 of the recorded abandonment inspection on real migrated file-backed SQLite (ADR-0071). Runs are
/// closed by the production abandonment command: from Created without a workspace and from Running at its preserved stage (with and
/// without workspace rows). The answer carries exactly the stored project, canonical reason, instant, supporting event identity and
/// positive sequence, never depends on present eligibility or on later unrelated work, survives a reopen and changes nothing.
/// </summary>
public sealed class RunAbandonmentInspectionTests : RunAbandonmentInspectionTestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_production_written_abandonment_is_recorded_with_its_exact_stored_facts(bool running, bool withWorkspace)
    {
        var closed = await AbandonAsync(running, withWorkspace);
        var before = await ChangeFingerprintAsync();

        var result = await InspectAsync(closed.RunId);

        Assert.Equal(closed.RunId, result.RunId);
        var recorded = Recorded(result);
        Assert.Equal(closed.ProjectId, recorded.ProjectId);
        Assert.Equal(AbandonmentRaceSupport.Reason, recorded.Reason);
        Assert.Equal(closed.AbandonedAt, recorded.AbandonedAtUtc);
        Assert.Equal(TimeSpan.Zero, recorded.AbandonedAtUtc.Offset);
        Assert.Equal(closed.EventId, recorded.EventId);
        Assert.Equal(closed.EventSequence, recorded.EventSequence);
        Assert.True(recorded.EventSequence > 0);
        Assert.Equal(before, await ChangeFingerprintAsync());
    }

    [Fact]
    public async Task The_closed_run_keeps_its_recorded_answer_whatever_its_lifecycle_stage_said_before()
    {
        var created = await AbandonAsync(running: false);
        var running = await AbandonAsync(running: true, withWorkspace: true);

        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, (await InspectAsync(created.RunId)).Status);
        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, (await InspectAsync(running.RunId)).Status);
        Assert.NotEqual(Recorded(await InspectAsync(created.RunId)).EventId, Recorded(await InspectAsync(running.RunId)).EventId);
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("Running")]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Interrupted")]
    public async Task A_recognized_run_that_was_never_abandoned_is_not_recorded(string lifecycle)
    {
        var scene = await AbandonmentScene.CreateAsync(Fixture, running: lifecycle != "Created");
        await scene.SqlAsync("UPDATE runs SET Lifecycle = {0} WHERE Id = {1}", lifecycle, scene.RunId);
        var before = await ChangeFingerprintAsync();

        var result = await InspectAsync(scene.RunId);

        Assert.Equal(RunAbandonmentInspectionStatus.NotRecorded, result.Status);
        Assert.Equal(scene.RunId, result.RunId);
        Assert.Null(result.Abandonment);
        Assert.Equal(before, await ChangeFingerprintAsync());
    }

    [Theory]
    [InlineData(RunExecutionMode.Legacy)]
    [InlineData(RunExecutionMode.Simulated)]
    public async Task A_recognized_non_manual_mode_without_abandonment_is_not_recorded(RunExecutionMode mode)
    {
        var scene = await AbandonmentScene.CreateAsync(Fixture, mode: mode);

        Assert.Equal(RunAbandonmentInspectionStatus.NotRecorded, (await InspectAsync(scene.RunId)).Status);
    }

    [Fact]
    public async Task A_newer_objective_in_the_same_project_is_not_recorded_while_the_earlier_closure_stays_recorded()
    {
        var earlier = await AbandonAsync(running: true);
        var newer = await AbandonmentScene.CreateAsync(Fixture, running: false);
        await newer.SqlAsync("UPDATE runs SET ProjectId = {0}, ExecutionNumber = 2 WHERE Id = {1}", Id(earlier.ProjectId), newer.RunId);

        var newerResult = await InspectAsync(newer.RunId);

        Assert.Equal(RunAbandonmentInspectionStatus.NotRecorded, newerResult.Status);
        Assert.Equal(earlier.EventId, Recorded(await InspectAsync(earlier.RunId)).EventId);
    }

    [Fact]
    public async Task Unrelated_events_of_the_run_do_not_become_an_abandonment()
    {
        var scene = await AbandonmentScene.CreateAsync(Fixture, running: true);
        await scene.AddAttemptAsync();

        Assert.Equal(RunAbandonmentInspectionStatus.NotRecorded, (await InspectAsync(scene.RunId)).Status);
    }

    [Fact]
    public async Task An_unknown_run_and_a_run_whose_registered_project_is_absent_are_one_fixed_not_found()
    {
        var closed = await AbandonAsync();
        var orphan = await AbandonAsync();
        await ExecuteAsync($"UPDATE runs SET ProjectId = '{Id(Guid.NewGuid())}' WHERE Id = '{Id(orphan.RunId)}'");

        var unknown = await TryInspectAsync(Guid.NewGuid());
        var missingProject = await TryInspectAsync(orphan.RunId);

        Assert.True(unknown.IsFailure);
        Assert.True(missingProject.IsFailure);
        Assert.Equal(RunAbandonmentInspectionErrors.NotFoundCode, Assert.Single(unknown.Errors).Code);
        Assert.Equal(unknown.Errors[0], Assert.Single(missingProject.Errors));
        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, (await InspectAsync(closed.RunId)).Status);
    }

    [Fact]
    public async Task A_nil_run_identity_is_a_fixed_validation_failure_that_repeats_nothing()
    {
        var failures = await new GetRunAbandonmentInspectionQueryValidator().ValidateAsync(new GetRunAbandonmentInspectionQuery(Guid.Empty));

        var failure = Assert.Single(failures.Errors);
        Assert.Equal(RunAbandonmentInspectionErrors.InvalidRunCode, failure.ErrorCode);
        Assert.Equal(RunAbandonmentInspectionErrors.InvalidRunMessage, failure.ErrorMessage);
        Assert.True((await new GetRunAbandonmentInspectionQueryValidator().ValidateAsync(new GetRunAbandonmentInspectionQuery(Guid.NewGuid()))).IsValid);
    }

    [Fact]
    public async Task The_answer_survives_later_unrelated_work_and_a_reopen_of_the_database()
    {
        var closed = await AbandonAsync(running: true);
        var first = Recorded(await InspectAsync(closed.RunId));

        var next = await AbandonmentScene.CreateAsync(Fixture, running: false);
        await next.SqlAsync("UPDATE runs SET ProjectId = {0}, ExecutionNumber = 2 WHERE Id = {1}", Id(closed.ProjectId), next.RunId);
        var second = await AbandonmentRaceSupport.AbandonAsync(
            Fixture, next.RunId, "A second objective was replaced.", AbandonmentRaceSupport.AbandonedAt.AddHours(1));
        Assert.True(second.IsSuccess, second.IsFailure ? second.Errors[0].Code : null);
        await closed.Scene.AddWorkspaceAsync();
        await closed.Scene.AddAttemptAsync();
        await closed.Scene.SqlAsync("UPDATE git_workspaces SET Status = 'MissingExternally' WHERE ProjectId = {0}", Id(closed.ProjectId));
        SqliteConnection.ClearAllPools();

        var reopened = Recorded(await InspectAsync(closed.RunId));

        Assert.Equal(first, reopened);
        Assert.Equal("A second objective was replaced.", Recorded(await InspectAsync(next.RunId)).Reason);
    }

    [Fact]
    public async Task A_multiline_reason_is_returned_exactly_as_stored()
    {
        const string reason = "Line one\nLine two\n\nLine four";
        var closed = await AbandonAsync(reason: reason);

        Assert.Equal(reason, Recorded(await InspectAsync(closed.RunId)).Reason);
    }

    [Fact]
    public async Task A_unicode_reason_including_a_surrogate_pair_is_returned_exactly_as_stored()
    {
        var reason = string.Concat("Unicode ", (char)0xE9, " ", (char)0x4E2D, " ", char.ConvertFromUtf32(0x1F600), " end");
        var closed = await AbandonAsync(reason: reason);

        Assert.Equal(reason, Recorded(await InspectAsync(closed.RunId)).Reason);
    }
}
