using DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>The recorded local-commit operation of one run (ADR-0070) over real migrated file-backed SQLite: all six supported status
/// tuples (Failed, Interrupted and NeedsAttention with and without the execution marker), the commit identity only for Completed, the
/// NotRecorded, Unavailable and 404 distinctions, later work, reopening and the validator.</summary>
public sealed class LocalCommitInspectionTests : LocalCommitInspectionTestBase
{
    public static TheoryData<LocalCommitInspectionShape> Shapes => new(Enum.GetValues<LocalCommitInspectionShape>());

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task Every_supported_tuple_is_recorded_with_its_exact_facts(LocalCommitInspectionShape shape)
    {
        var rows = await SeedAsync(shape);

        var result = await InspectAsync(rows.Run.Id);

        Assert.Equal(rows.Run.Id, result.RunId);
        var operation = Recorded(result);
        var status = LocalCommitInspectionSeed.StatusOf(shape);
        Assert.Equal(rows.Operation.Id, operation.OperationId);
        Assert.Equal(rows.Project.Id, operation.ProjectId);
        Assert.Equal(rows.Workspace.Id, operation.WorkspaceId);
        Assert.Equal(rows.Checkpoint.Id, operation.CheckpointId);
        Assert.Equal(rows.Checkpoint.CheckpointNumber, operation.CheckpointNumber);
        Assert.Equal(LocalCommitInspectionSeed.Fingerprint, operation.CheckpointFingerprintSha256);
        Assert.Equal(status, operation.Status);
        Assert.Equal(rows.Workspace.BranchName, operation.BranchName);
        Assert.Equal(LocalCommitInspectionSeed.Parent, operation.ParentCommitSha);
        Assert.Equal(LocalCommitInspectionSeed.Tree, operation.TreeSha);
        Assert.Equal(1, operation.ChangedPathCount);
        Assert.Equal(10, operation.TotalBytes);
        Assert.Equal(LocalCommitInspectionSeed.Created, operation.CreatedAtUtc);

        var started = shape is LocalCommitInspectionShape.Executing or LocalCommitInspectionShape.Completed
            or LocalCommitInspectionShape.FailedStarted or LocalCommitInspectionShape.InterruptedStarted
            or LocalCommitInspectionShape.AttentionStarted;
        Assert.Equal(started ? (DateTimeOffset?)LocalCommitInspectionSeed.Started : null, operation.ExecutionStartedAtUtc);
        var finished = status is LocalCommitStatus.Completed or LocalCommitStatus.Failed or LocalCommitStatus.Interrupted;
        Assert.Equal(finished ? (DateTimeOffset?)LocalCommitInspectionSeed.Finished : null, operation.CompletedAtUtc);

        // The commit identity is returned only for Completed; the prepared proposed commit never escapes for any other state.
        Assert.Equal(status == LocalCommitStatus.Completed ? LocalCommitInspectionSeed.ProposedCommit : null, operation.CommitSha);
        var reasonRequired = status is LocalCommitStatus.Failed or LocalCommitStatus.Interrupted or LocalCommitStatus.NeedsAttention;
        Assert.Equal(
            reasonRequired
                ? new RecordedLocalCommitReason(RecordedLocalCommitReasonKind.Known, "local_commit.git_unprovable")
                : RecordedLocalCommitReason.None,
            operation.Reason);
    }

    [Fact]
    public async Task A_run_without_an_operation_is_NotRecorded_and_says_nothing_else()
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Prepared);
        await ExecuteAsync($"DELETE FROM local_commit_authority_members WHERE OperationId = '{Id(rows.Operation.Id)}'");
        Assert.Equal(1, await ExecuteAsync($"DELETE FROM local_commit_operations WHERE RunId = '{Id(rows.Run.Id)}'"));

        var result = await InspectAsync(rows.Run.Id);

        Assert.Equal(LocalCommitInspectionStatus.NotRecorded, result.Status);
        Assert.Null(result.Operation);
        Assert.Equal(rows.Run.Id, result.RunId);
    }

    [Fact]
    public async Task A_second_run_of_the_project_without_an_operation_is_NotRecorded_beside_a_recorded_one()
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Completed);
        await using (var db = Fixture.CreateContext())
        {
            var newer = Run.RecordIntent(Guid.NewGuid(), rows.Project.Id, 2, "Later", LocalCommitRowsSeed.Now.AddHours(1));
            db.Runs.Add(newer);
            await db.SaveChangesAsync();
            Assert.Equal(LocalCommitInspectionStatus.NotRecorded, (await InspectAsync(newer.Id)).Status);
        }

        Assert.Equal(LocalCommitInspectionStatus.Recorded, (await InspectAsync(rows.Run.Id)).Status);
    }

    [Fact]
    public async Task An_unknown_run_is_the_fixed_not_found()
    {
        await SeedAsync(LocalCommitInspectionShape.Completed);

        var result = await TryInspectAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(LocalCommitInspectionErrors.NotFoundCode, result.Errors[0].Code);
        Assert.Equal("The requested local commit inspection was not found.", result.Errors[0].Description);
    }

    [Fact]
    public async Task A_run_whose_registered_project_is_absent_is_the_same_fixed_not_found()
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Completed);
        Assert.Equal(1, await ExecuteAsync($"DELETE FROM projects WHERE Id = '{Id(rows.Project.Id)}'"));

        var result = await TryInspectAsync(rows.Run.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(LocalCommitInspectionErrors.NotFoundCode, result.Errors[0].Code);
    }

    [Fact]
    public void The_nil_run_identity_is_refused_by_the_validator_with_fixed_text()
    {
        var failures = new GetLocalCommitInspectionQueryValidator().Validate(new GetLocalCommitInspectionQuery(Guid.Empty)).Errors;

        var failure = Assert.Single(failures);
        Assert.Equal(LocalCommitInspectionErrors.InvalidRunCode, failure.ErrorCode);
        Assert.Equal(LocalCommitInspectionErrors.InvalidRunMessage, failure.ErrorMessage);
        Assert.Empty(new GetLocalCommitInspectionQueryValidator().Validate(new GetLocalCommitInspectionQuery(Guid.NewGuid())).Errors);
    }

    [Fact]
    public async Task A_historical_operation_survives_later_work_recipe_review_and_lifecycle_changes()
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.FailedStarted);
        var before = Recorded(await InspectAsync(rows.Run.Id));

        await using (var db = Fixture.CreateContext())
        {
            // Later work in the same workspace: a newer checkpoint, a changed workspace state and an abandoned run lifecycle.
            var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == rows.Workspace.Id);
            var newer = GitCheckpoint.Capture(
                Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), LocalCommitRowsSeed.Now.AddHours(2),
                new string('9', 40), new string('8', 64), []);
            db.GitCheckpoints.Add(newer);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE git_workspaces SET Status = 'MissingExternally', LastFailureReasonCode = 'workspace_missing' WHERE Id = {workspace.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET Lifecycle = 'Abandoned', Stage = 'Intake' WHERE Id = {rows.Run.Id}");
        }

        var after = Recorded(await InspectAsync(rows.Run.Id));

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_historical_operation_is_the_same_after_the_file_backed_database_is_reopened()
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Completed);
        var before = Recorded(await InspectAsync(rows.Run.Id));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        await using var reopened = new DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext(Fixture.Options);
        var after = await new GetLocalCommitInspectionQueryHandler(reopened).HandleAsync(
            new GetLocalCommitInspectionQuery(rows.Run.Id), CancellationToken.None);

        Assert.True(after.IsSuccess);
        Assert.Equal(before, Recorded(after.Value));
    }

    [Fact]
    public async Task A_later_operation_of_another_run_in_the_same_workspace_does_not_change_the_answer()
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Completed);
        var before = Recorded(await InspectAsync(rows.Run.Id));
        await using (var db = Fixture.CreateContext())
        {
            await LocalCommitRowsSeed.AddOperationAsync(
                db, rows.Project, rows.Workspace, rows.Lease, LocalCommitStatus.Prepared, new string('7', 40), new string('6', 40));
        }

        Assert.Equal(before, Recorded(await InspectAsync(rows.Run.Id)));
    }

    [Fact]
    public async Task Inspection_changes_no_durable_fact_in_any_recorded_shape()
    {
        foreach (var shape in Enum.GetValues<LocalCommitInspectionShape>())
        {
            var rows = await SeedAsync(shape);
            var before = await ChangeFingerprintAsync();

            Assert.Equal(LocalCommitInspectionStatus.Recorded, (await InspectAsync(rows.Run.Id)).Status);

            Assert.Equal(before, await ChangeFingerprintAsync());
        }
    }
}
