using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>
/// ADR-0069 selected metadata over real migrated file-backed SQLite: an exact execution of the project is proved by identity (never by
/// time, the current run, a shared checkpoint, Agent membership or a sequence), every supported result tuple is reported as recorded
/// bookkeeping, a legitimate completion fingerprint that differs from the claim is preserved for SourceChanged, later work and recipe edits
/// do not invalidate coherent history, and the two sealed-stream descriptors are admitted independently.
/// </summary>
public sealed class ProjectVerificationInspectionTests : ProjectVerificationInspectionTestBase
{
    private async Task<(Guid ProjectId, GitWorkspace Workspace, GitCheckpoint Checkpoint, VerificationCommand Command)> SeedScopeAsync(
        string commandName = "Unit tests")
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId, 1, commandName);
        return (projectId, workspace, checkpoint, command);
    }

    [Fact]
    public async Task An_unknown_project_an_unknown_execution_and_a_foreign_execution_are_the_same_fixed_non_echoing_not_found()
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync();
        var execution = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1);
        var (otherProject, otherWorkspace, otherCheckpoint, otherCommand) = await SeedScopeAsync();
        var foreign = await Seed.SeedExecutionAsync(otherProject, otherWorkspace, otherCheckpoint, otherCommand, 1);
        var unknownProject = Guid.NewGuid();
        var unknownExecution = Guid.NewGuid();

        var results = new[]
        {
            await TryInspectAsync(unknownProject, execution.Id),
            await TryInspectAsync(projectId, unknownExecution),
            await TryInspectAsync(projectId, foreign.Id),
            await TryInspectAsync(otherProject, execution.Id),
        };

        Assert.All(results, result =>
        {
            Assert.True(result.IsFailure);
            var error = Assert.Single(result.Errors);
            Assert.Equal(ProjectVerificationInspectionErrors.NotFoundCode, error.Code);
            Assert.Equal(ProjectVerificationInspectionErrors.NotFound().Description, error.Description);
        });
        Assert.True((await TryInspectAsync(projectId, execution.Id)).IsSuccess);
    }

    [Fact]
    public async Task A_foreign_execution_stays_not_found_even_when_its_own_storage_is_damaged()
    {
        var (projectId, _, _, _) = await SeedScopeAsync();
        var (otherProject, otherWorkspace, otherCheckpoint, otherCommand) = await SeedScopeAsync();
        var foreign = await Seed.SeedExecutionAsync(otherProject, otherWorkspace, otherCheckpoint, otherCommand, 1);
        await ExecuteAsync($"UPDATE verification_executions SET Status = 'Bogus', ClaimedAtUtc = 'garbage' WHERE Id = {foreign.Id}");

        var result = await TryInspectAsync(projectId, foreign.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectVerificationInspectionErrors.NotFoundCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task An_older_manual_execution_beyond_the_latest_twenty_is_exact_beside_newer_work_with_no_agent_or_sequence_link()
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync("Manual build");
        var oldest = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1, ExecutionShape.FailedExited, 7);
        for (var number = 2; number <= 30; number++)
        {
            await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, number);
        }

        var newerCheckpoint = await Seed.AddCheckpointAsync(workspace, 2, new string('d', 64));
        await using (var dbContext = Fixture.CreateContext())
        {
            dbContext.Runs.Add(Run.RecordClassifiedIntent(
                Guid.NewGuid(), projectId, 1, RunExecutionMode.ManualAgent, "Newer run", ProjectVerificationInspectionSeed.Now.AddHours(1)));
            await dbContext.SaveChangesAsync();
        }

        await Seed.SeedExecutionAsync(projectId, workspace, newerCheckpoint, command, 31, ExecutionShape.RunningDispatched);
        var stdout = await Seed.AddOutputAsync(oldest.Id, VerificationOutputPurpose.StandardOutput, "v/out.sealed", ProjectVerificationInspectionSeed.ContentHash(), 42);

        var result = await InspectAsync(projectId, oldest.Id);

        Assert.Equal(ProjectVerificationInspectionStatus.Recorded, result.Status);
        var execution = Assert.IsType<ProjectVerificationInspectionExecution>(result.Execution);
        Assert.Equal((projectId, oldest.Id), (result.ProjectId, result.ExecutionId));
        Assert.Equal((1, command.Id, "Manual build"), (execution.ExecutionNumber, execution.CommandId, execution.CommandName));
        Assert.Equal((workspace.Id, checkpoint.Id, 1), (execution.WorkspaceId, execution.CheckpointId, execution.CheckpointNumber));
        Assert.Equal((ProjectVerificationInspectionSeed.Fingerprint, ProjectVerificationInspectionSeed.Fingerprint),
            (execution.ClaimFingerprintSha256, execution.CompletionFingerprintSha256));
        Assert.Equal((VerificationExecutionStatus.Failed, VerificationExecutionOutcome.Exited, 7), (execution.Status, execution.Outcome, execution.ExitCode));
        Assert.Equal(ProjectVerificationInspectionSeed.Now.AddSeconds(1), execution.ClaimedAtUtc);
        Assert.Equal(ProjectVerificationInspectionSeed.Now.AddSeconds(1).AddTicks(10_000_001), execution.DispatchedAtUtc);
        Assert.Equal(ProjectVerificationInspectionSeed.Now.AddSeconds(1).AddTicks(10_000_001 + 20_000_003), execution.CompletedAtUtc);
        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stdout.Id, 42, VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, false),
            execution.StandardOutput);
        Assert.Equal(RecordedVerificationOutputDescriptor.NotRecorded, execution.StandardError);
        await using var verify = Fixture.CreateContext();
        Assert.Empty(verify.AttemptVerificationEvidence);
        Assert.Empty(verify.Attempts);
        Assert.Empty(verify.LocalExecutionSequenceSteps);
    }

    public static TheoryData<ExecutionShape> AllShapes() => new(Enum.GetValues<ExecutionShape>());

    [Theory]
    [MemberData(nameof(AllShapes))]
    public async Task Every_supported_result_tuple_is_reported_exactly_as_recorded_bookkeeping(ExecutionShape shape)
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync();
        var seeded = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 4, shape, 9);

        var result = await InspectAsync(projectId, seeded.Id);

        Assert.Equal(ProjectVerificationInspectionStatus.Recorded, result.Status);
        var execution = Assert.IsType<ProjectVerificationInspectionExecution>(result.Execution);
        var (status, outcome, exitCode, completion, dispatched, completed) = shape switch
        {
            ExecutionShape.Running => (VerificationExecutionStatus.Running, (VerificationExecutionOutcome?)null, (int?)null, (string?)null, false, false),
            ExecutionShape.RunningDispatched => (VerificationExecutionStatus.Running, null, null, null, true, false),
            ExecutionShape.Passed => (VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0, ProjectVerificationInspectionSeed.Fingerprint, true, true),
            ExecutionShape.FailedExited => (VerificationExecutionStatus.Failed, VerificationExecutionOutcome.Exited, 9, ProjectVerificationInspectionSeed.Fingerprint, true, true),
            ExecutionShape.FailedOutcome => (VerificationExecutionStatus.Failed, VerificationExecutionOutcome.Failed, null, ProjectVerificationInspectionSeed.Fingerprint, true, true),
            ExecutionShape.TimedOut => (VerificationExecutionStatus.TimedOut, VerificationExecutionOutcome.TimedOut, null, ProjectVerificationInspectionSeed.Fingerprint, true, true),
            ExecutionShape.Cancelled => (VerificationExecutionStatus.Cancelled, VerificationExecutionOutcome.Cancelled, null, ProjectVerificationInspectionSeed.Fingerprint, true, true),
            ExecutionShape.Interrupted => (VerificationExecutionStatus.Interrupted, null, null, null, false, true),
            ExecutionShape.InterruptedDispatched => (VerificationExecutionStatus.Interrupted, null, null, null, true, true),
            ExecutionShape.SourceChangedBeforeDispatch => (VerificationExecutionStatus.SourceChanged, null, null, ProjectVerificationInspectionSeed.OtherFingerprint, false, true),
            ExecutionShape.SourceChangedExited => (VerificationExecutionStatus.SourceChanged, VerificationExecutionOutcome.Exited, 9, ProjectVerificationInspectionSeed.OtherFingerprint, true, true),
            ExecutionShape.SourceChangedTimedOut => (VerificationExecutionStatus.SourceChanged, VerificationExecutionOutcome.TimedOut, null, ProjectVerificationInspectionSeed.OtherFingerprint, true, true),
            _ => (VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, null, null, null, false, true),
        };
        Assert.Equal((status, outcome, exitCode), (execution.Status, execution.Outcome, execution.ExitCode));
        Assert.Equal(ProjectVerificationInspectionSeed.Fingerprint, execution.ClaimFingerprintSha256);
        Assert.Equal(completion, execution.CompletionFingerprintSha256);
        Assert.Equal(dispatched, execution.DispatchedAtUtc is not null);
        Assert.Equal(completed, execution.CompletedAtUtc is not null);
        Assert.True(execution.DispatchedAtUtc is null || execution.DispatchedAtUtc >= execution.ClaimedAtUtc);
        Assert.True(execution.CompletedAtUtc is null || execution.CompletedAtUtc >= (execution.DispatchedAtUtc ?? execution.ClaimedAtUtc));
    }

    public static TheoryData<int> ExitCodes() => new(
        int.MinValue, -int.MaxValue, -int.MaxValue + 1, -2, -1, 1, 2, int.MaxValue - 1, int.MaxValue);

    [Theory]
    [MemberData(nameof(ExitCodes))]
    public async Task Every_nonzero_int32_exit_code_is_recorded_exactly_including_the_extrema(int exitCode)
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync();
        var failed = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1, ExecutionShape.FailedExited, exitCode);
        var changed = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 2, ExecutionShape.SourceChangedExited, exitCode);

        var failedResult = await InspectAsync(projectId, failed.Id);
        var changedResult = await InspectAsync(projectId, changed.Id);

        Assert.Equal((VerificationExecutionStatus.Failed, exitCode), (failedResult.Execution!.Status, failedResult.Execution.ExitCode));
        Assert.Equal((VerificationExecutionStatus.SourceChanged, exitCode), (changedResult.Execution!.Status, changedResult.Execution.ExitCode));
    }

    [Fact]
    public async Task An_exit_code_of_zero_with_a_different_completion_fingerprint_is_a_legitimate_source_changed_record()
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync();
        var seeded = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1, ExecutionShape.SourceChangedExited, 0);

        var execution = (await InspectAsync(projectId, seeded.Id)).Execution!;

        Assert.Equal((VerificationExecutionStatus.SourceChanged, VerificationExecutionOutcome.Exited, 0), (execution.Status, execution.Outcome, execution.ExitCode));
        Assert.NotEqual(execution.ClaimFingerprintSha256, execution.CompletionFingerprintSha256);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task The_valid_extreme_execution_and_checkpoint_numbers_are_exact(int number)
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId, number);
        var command = await Seed.SeedCommandAsync(projectId);
        var seeded = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, number);

        var execution = (await InspectAsync(projectId, seeded.Id)).Execution!;

        Assert.Equal((number, number), (execution.ExecutionNumber, execution.CheckpointNumber));
    }

    [Fact]
    public async Task Later_work_a_changed_disabled_or_removed_recipe_and_a_newer_checkpoint_never_invalidate_coherent_history()
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync("Original recipe");
        var seeded = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1, ExecutionShape.Passed);
        var before = (await InspectAsync(projectId, seeded.Id)).Execution!;

        await Seed.AddCheckpointAsync(workspace, 2, new string('e', 64));
        await Seed.SeedWorkspaceAsync(projectId, 1, new string('f', 64));
        await ExecuteAsync($"UPDATE verification_commands SET Name = 'Edited', IsEnabled = 0, ExecutablePath = 'C:\\other.exe' WHERE Id = {command.Id}");
        var edited = (await InspectAsync(projectId, seeded.Id)).Execution!;
        await ExecuteAsync($"DELETE FROM verification_commands WHERE Id = {command.Id}");
        var removed = (await InspectAsync(projectId, seeded.Id)).Execution!;

        Assert.Equal(before, edited);
        Assert.Equal(before, removed);
        Assert.Equal("Original recipe", removed.CommandName);
    }

    [Fact]
    public async Task A_reopened_context_returns_the_same_recorded_metadata()
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync();
        var seeded = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1);

        var first = await InspectAsync(projectId, seeded.Id);
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(Fixture.ConnectionString));
        var reopened = await InspectAsync(projectId, seeded.Id);

        Assert.Equal(first, reopened);
    }

    [Fact]
    public async Task Each_stream_descriptor_preserves_the_recorded_known_or_recovered_capture_tuple_independently()
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync();
        var seeded = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1);
        var stdout = await Seed.AddOutputAsync(
            seeded.Id, VerificationOutputPurpose.StandardOutput, "a/out", ProjectVerificationInspectionSeed.ContentHash(), 0, true);
        var stderr = await Seed.AddOutputAsync(
            seeded.Id, VerificationOutputPurpose.StandardError, "a/err", ProjectVerificationInspectionSeed.ContentHash('c'), 9_007_199_254_740_991,
            null, VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption);

        var execution = (await InspectAsync(projectId, seeded.Id)).Execution!;

        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stdout.Id, 0, VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, true),
            execution.StandardOutput);
        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stderr.Id, 9_007_199_254_740_991,
                VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption, null),
            execution.StandardError);
    }

    public static TheoryData<string> DescriptorDefectNames() => new(
        "length-real", "length-text", "length-negative", "length-beyond-safe-integer", "length-blob", "truncated-two", "truncated-text",
        "capture-unknown", "capture-recovered-with-truncation", "capture-known-without-truncation", "artifact-id-empty", "artifact-id-garbage",
        "artifact-id-lowercase", "execution-id-garbage", "second-row");

    [Theory]
    [MemberData(nameof(DescriptorDefectNames))]
    public async Task A_damaged_stream_descriptor_is_unavailable_without_hiding_its_sibling_or_the_execution(string defect)
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync();
        var seeded = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1);
        var stdout = await Seed.AddOutputAsync(seeded.Id, VerificationOutputPurpose.StandardOutput, "a/out", ProjectVerificationInspectionSeed.ContentHash(), 12);
        var stderr = await Seed.AddOutputAsync(seeded.Id, VerificationOutputPurpose.StandardError, "a/err", ProjectVerificationInspectionSeed.ContentHash('c'), 7);
        FormattableString damage = defect switch
        {
            "length-real" => $"UPDATE verification_output_artifacts SET ByteLength = 1.5 WHERE Id = {stdout.Id}",
            "length-text" => $"UPDATE verification_output_artifacts SET ByteLength = 'twelve' WHERE Id = {stdout.Id}",
            "length-negative" => $"UPDATE verification_output_artifacts SET ByteLength = -1 WHERE Id = {stdout.Id}",
            "length-beyond-safe-integer" => $"UPDATE verification_output_artifacts SET ByteLength = 9007199254740992 WHERE Id = {stdout.Id}",
            "length-blob" => $"UPDATE verification_output_artifacts SET ByteLength = X'0102' WHERE Id = {stdout.Id}",
            "truncated-two" => $"UPDATE verification_output_artifacts SET Truncated = 2 WHERE Id = {stdout.Id}",
            "truncated-text" => $"UPDATE verification_output_artifacts SET Truncated = 'yes' WHERE Id = {stdout.Id}",
            "capture-unknown" => $"UPDATE verification_output_artifacts SET CaptureOutcome = 'Weird' WHERE Id = {stdout.Id}",
            "capture-recovered-with-truncation" => $"UPDATE verification_output_artifacts SET CaptureOutcome = 'RecoveredAfterHostInterruption' WHERE Id = {stdout.Id}",
            "capture-known-without-truncation" => $"UPDATE verification_output_artifacts SET Truncated = NULL WHERE Id = {stdout.Id}",
            "artifact-id-empty" => $"UPDATE verification_output_artifacts SET Id = {Guid.Empty} WHERE Id = {stdout.Id}",
            "artifact-id-garbage" => $"UPDATE verification_output_artifacts SET Id = 'not-a-guid' WHERE Id = {stdout.Id}",
            "artifact-id-lowercase" => $"UPDATE verification_output_artifacts SET Id = lower(Id) WHERE Id = {stdout.Id}",
            "execution-id-garbage" => $"UPDATE verification_output_artifacts SET VerificationExecutionId = lower(VerificationExecutionId) WHERE Id = {stdout.Id}",
            _ => $"INSERT INTO verification_output_artifacts (Id, VerificationExecutionId, Purpose, RelativeStoragePath, ContentHash, ByteLength, Truncated, CaptureOutcome, CreatedAtUtc) SELECT {Guid.NewGuid()}, VerificationExecutionId, Purpose, RelativeStoragePath, ContentHash, ByteLength, Truncated, CaptureOutcome, CreatedAtUtc FROM verification_output_artifacts WHERE Id = {stdout.Id}",
        };
        if (defect == "second-row")
        {
            await ExecuteAsync($"DROP INDEX IF EXISTS IX_verification_output_artifacts_VerificationExecutionId_Purpose");
        }

        await ExecuteAsync(damage);

        var result = await InspectAsync(projectId, seeded.Id);
        Assert.Equal(ProjectVerificationInspectionStatus.Recorded, result.Status);
        var execution = result.Execution!;
        var expectedOut = defect == "execution-id-garbage"
            ? RecordedVerificationOutputDescriptor.NotRecorded
            : RecordedVerificationOutputDescriptor.Unavailable;
        Assert.Equal(expectedOut, execution.StandardOutput);
        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stderr.Id, 7, VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, false),
            execution.StandardError);
    }

    [Fact]
    public async Task A_row_with_a_purpose_outside_the_allowlist_is_never_read_as_a_supported_stream()
    {
        var (projectId, workspace, checkpoint, command) = await SeedScopeAsync();
        var seeded = await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, 1);
        var stdout = await Seed.AddOutputAsync(seeded.Id, VerificationOutputPurpose.StandardOutput, "a/out", ProjectVerificationInspectionSeed.ContentHash(), 12);
        await ExecuteAsync($"UPDATE verification_output_artifacts SET Purpose = 'Weird' WHERE Id = {stdout.Id}");

        var execution = (await InspectAsync(projectId, seeded.Id)).Execution!;

        Assert.Equal(RecordedVerificationOutputDescriptor.NotRecorded, execution.StandardOutput);
        Assert.Equal(RecordedVerificationOutputDescriptor.NotRecorded, execution.StandardError);
    }
}
