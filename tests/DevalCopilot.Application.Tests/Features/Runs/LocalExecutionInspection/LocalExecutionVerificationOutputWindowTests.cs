using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionVerificationOutputWindow;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection.LocalExecutionInspectionHarness;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;

/// <summary>Sealed windows of one selected verification step (ADR-0061): the whole structure and the selected verification and pinned
/// recipe association are re-proved on every request before any artifact is opened, only the proved execution's requested stream is read
/// through the artifact store's sealed boundary, an unproved association never names or reads the reserved identity, and every refusal
/// reads zero bytes and keeps the requested offset.</summary>
public sealed class LocalExecutionVerificationOutputWindowTests : IAsyncLifetime
{
    private const VerificationOutputPurpose Out = VerificationOutputPurpose.StandardOutput;
    private const VerificationOutputPurpose Err = VerificationOutputPurpose.StandardError;

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionInspectionHarness Harness, Guid SequenceId)> CompletedAsync()
    {
        var (harness, sequenceId) = await ArmedAsync(fixture);
        await harness.Scene.DriveAsync(sequenceId, AllKindsScript());
        return (harness, sequenceId);
    }

    private static string Op(Guid id) => $"'{id.ToString().ToUpperInvariant()}'";

    private static void AssertRefused(
        GetLocalExecutionVerificationOutputWindowQueryResult result, LocalExecutionVerificationOutputWindowStatus status, long offset)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Artifact);
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(offset, result.NextOffset);
        Assert.Equal(0, result.TotalLength);
    }

    [Fact]
    public async Task A_failed_verification_serves_exactly_the_requested_stream_and_reads_only_its_sealed_path()
    {
        var (harness, sequenceId) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        var outputs = (await harness.OutputsAsync(row.OperationId!.Value)).ToDictionary(output => output.Purpose);

        var stdout = await harness.WindowAsync(row.Id, Out);

        Assert.Equal(LocalExecutionVerificationOutputWindowStatus.Ok, stdout.Status);
        Assert.Equal(harness.Scene.RunId, stdout.RunId);
        Assert.Equal(sequenceId, stdout.SequenceId);
        Assert.Equal(row.Id, stdout.StepId);
        Assert.Equal(row.OperationId, stdout.ExecutionId);
        Assert.Equal(Out, stdout.Stream);
        Assert.Equal("stdout failure text", stdout.Text);
        Assert.Equal(19, stdout.NextOffset);
        Assert.Equal(19, stdout.TotalLength);
        Assert.Equal(outputs[Out].Id, stdout.Artifact!.ArtifactId);
        Assert.Equal(outputs[Out].RelativeStoragePath, Assert.Single(harness.Store.Reads));

        var stderr = await harness.WindowAsync(row.Id, Err, 7, 64);

        Assert.Equal(LocalExecutionVerificationOutputWindowStatus.Ok, stderr.Status);
        Assert.Equal("failure text", stderr.Text);
        Assert.Equal(19, stderr.NextOffset);
        Assert.Equal(19, stderr.TotalLength);
        Assert.Equal(
            new[] { outputs[Out].RelativeStoragePath, outputs[Err].RelativeStoragePath }, harness.Store.Reads);
    }

    [Fact]
    public async Task A_window_at_the_exact_end_is_empty_and_an_offset_past_the_recorded_length_is_refused_before_any_read()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);

        var atEnd = await harness.WindowAsync(row.Id, Out, 19);
        Assert.Equal(LocalExecutionVerificationOutputWindowStatus.Ok, atEnd.Status);
        Assert.Equal(string.Empty, atEnd.Text);
        Assert.Equal(19, atEnd.NextOffset);
        var readsAfterEnd = harness.Store.Reads.Count;

        var past = await harness.TryWindowAsync(row.Id, Out, 20);

        Assert.True(past.IsFailure);
        Assert.Equal("validation.invalid_offset", past.Errors[0].Code);
        Assert.DoesNotContain("20", past.Errors[0].ToString(), StringComparison.Ordinal);
        Assert.Equal(readsAfterEnd, harness.Store.Reads.Count);
    }

    [Theory]
    [InlineData(-1L, 64)]
    [InlineData(GetLocalExecutionVerificationOutputWindowQuery.MaximumOffset + 1, 64)]
    [InlineData(0L, 63)]
    [InlineData(0L, 65537)]
    [InlineData(0L, 0)]
    [InlineData(0L, -64)]
    public async Task An_offset_or_cap_outside_the_admitted_bounds_is_refused_not_clamped_and_reads_nothing(long offset, int maxBytes)
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);

        var refused = await harness.TryWindowAsync(row.Id, Out, offset, maxBytes);

        Assert.True(refused.IsFailure);
        Assert.StartsWith("validation.invalid_", refused.Errors[0].Code, StringComparison.Ordinal);
        Assert.Empty(harness.Store.Reads);
    }

    [Fact]
    public async Task An_unknown_run_a_run_without_a_sequence_a_foreign_step_an_agent_step_and_an_unsupported_stream_are_one_fixed_not_found()
    {
        var (harness, _) = await CompletedAsync();
        var verification = await harness.RowAsync(1);
        var agent = await harness.RowAsync(7);
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture);

        var results = new[]
        {
            await harness.TryWindowAsync(verification.Id, Out, 0, 64, Guid.NewGuid()),
            await harness.TryWindowAsync(verification.Id, Out, 0, 64, other.RunId),
            await harness.TryWindowAsync(Guid.NewGuid(), Out),
            await harness.TryWindowAsync(Guid.Empty, Out),
            await harness.TryWindowAsync(agent.Id, Out),
            await harness.TryWindowAsync(verification.Id, (VerificationOutputPurpose)7),
        };

        foreach (var result in results)
        {
            Assert.True(result.IsFailure);
            Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, result.Errors[0].Code);
        }

        Assert.Empty(harness.Store.Reads);
    }

    [Fact]
    public async Task A_passed_verification_without_sealed_output_is_not_recorded_with_its_proved_execution_and_reads_nothing()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(2);

        var window = await harness.WindowAsync(row.Id, Out, 5);

        AssertRefused(window, LocalExecutionVerificationOutputWindowStatus.NotRecorded, 5);
        Assert.Equal(row.OperationId, window.ExecutionId);
        Assert.Empty(harness.Store.Reads);
    }

    [Fact]
    public async Task A_missing_file_and_a_tampered_file_are_distinct_refusals_with_no_bytes_and_the_requested_offset()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        var outputs = (await harness.OutputsAsync(row.OperationId!.Value)).ToDictionary(output => output.Purpose);
        harness.Scene.Artifacts.Remove(outputs[Out].RelativeStoragePath);
        harness.Scene.Artifacts.Overwrite(outputs[Err].RelativeStoragePath, "tampered bytes!!!!!!"u8.ToArray());

        var missing = await harness.WindowAsync(row.Id, Out, 4);
        var tampered = await harness.WindowAsync(row.Id, Err, 4);

        AssertRefused(missing, LocalExecutionVerificationOutputWindowStatus.Missing, 4);
        AssertRefused(tampered, LocalExecutionVerificationOutputWindowStatus.IntegrityMismatch, 4);
        Assert.Equal(row.OperationId, missing.ExecutionId);
        Assert.Equal(row.OperationId, tampered.ExecutionId);
    }

    [Theory]
    [InlineData("rooted_path", "UPDATE verification_output_artifacts SET RelativeStoragePath = 'C:\\\\outside\\\\file' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("blank_path", "UPDATE verification_output_artifacts SET RelativeStoragePath = '   ' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("oversized_path", "UPDATE verification_output_artifacts SET RelativeStoragePath = hex(zeroblob(2000)) WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("hash_wrong_form", "UPDATE verification_output_artifacts SET ContentHash = 'md5:abc' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("hash_uppercase", "UPDATE verification_output_artifacts SET ContentHash = upper(ContentHash) WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("hash_oversized", "UPDATE verification_output_artifacts SET ContentHash = hex(zeroblob(100)) WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("length_negative", "UPDATE verification_output_artifacts SET ByteLength = -3 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("capture_unknown", "UPDATE verification_output_artifacts SET CaptureOutcome = 'Bogus' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_two", "UPDATE verification_output_artifacts SET Truncated = 2 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_negative_one", "UPDATE verification_output_artifacts SET Truncated = -1 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_seven", "UPDATE verification_output_artifacts SET Truncated = 7 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_real", "UPDATE verification_output_artifacts SET Truncated = 1.5 WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_text", "UPDATE verification_output_artifacts SET Truncated = 'x' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_blob", "UPDATE verification_output_artifacts SET Truncated = X'0102' WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    [InlineData("truncated_oversized", "UPDATE verification_output_artifacts SET Truncated = replace(hex(zeroblob(150)), '00', '1') WHERE VerificationExecutionId = {op} AND Purpose = 'StandardOutput'")]
    public async Task An_inadmissible_stream_row_is_unavailable_with_the_proved_execution_and_never_reaches_the_store(string name, string sql)
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        Assert.NotEmpty(name);
        await harness.CorruptAsync(sql.Replace("{op}", Op(row.OperationId!.Value), StringComparison.Ordinal));

        var window = await harness.WindowAsync(row.Id, Out, 2);

        AssertRefused(window, LocalExecutionVerificationOutputWindowStatus.Unavailable, 2);
        Assert.Equal(row.OperationId, window.ExecutionId);
        Assert.Empty(harness.Store.Reads);
        Assert.Equal(LocalExecutionVerificationOutputWindowStatus.Ok, (await harness.WindowAsync(row.Id, Err)).Status);
    }

    [Fact]
    public async Task A_verification_step_without_an_actual_link_is_unavailable_with_no_execution_identity_even_if_the_reserved_one_has_output()
    {
        var (harness, sequenceId) = await ArmedAsync(fixture);
        await harness.Scene.DriveAsync(sequenceId, new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).Verify(1, 1, LeaveRunning));
        var failed = await harness.RowAsync(Step.Verification, 1, 0);
        var running = await harness.RowAsync(Step.Verification, 1, 1);
        Assert.Equal(running.OperationId, running.ReservedOperationId);
        var op = Op(running.OperationId!.Value);
        await harness.CorruptAsync(
            "INSERT INTO verification_output_artifacts (Id, VerificationExecutionId, Purpose, RelativeStoragePath, ContentHash, ByteLength, "
            + "Truncated, CaptureOutcome, CreatedAtUtc) SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4000-8000-' || "
            + $"lower(hex(randomblob(6))), {op}, Purpose, RelativeStoragePath, ContentHash, ByteLength, Truncated, CaptureOutcome, CreatedAtUtc "
            + $"FROM verification_output_artifacts WHERE VerificationExecutionId = {Op(failed.OperationId!.Value)};"
            + "UPDATE local_execution_sequence_steps SET State = 'Reserved', OperationId = NULL, ClaimedAtUtc = NULL, ResultDeadlineUtc = NULL "
            + $"WHERE Id = {Op(running.Id)};",
            null,
            StepImmutable);

        var index = await harness.IndexAsync();
        Assert.Equal(DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceInspection.LocalExecutionSequenceInspectionStatus.Recorded, index.Status);
        Assert.Null(index.Steps[^1].OperationId);

        foreach (var stream in new[] { Out, Err })
        {
            var window = await harness.WindowAsync(running.Id, stream, 1);

            AssertRefused(window, LocalExecutionVerificationOutputWindowStatus.Unavailable, 1);
            Assert.Null(window.ExecutionId);
        }

        Assert.Empty(harness.Store.Reads);
    }

    [Fact]
    public async Task Every_window_re_proves_the_association_so_a_later_argument_drift_refuses_the_next_window_before_any_read()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        Assert.Equal(LocalExecutionVerificationOutputWindowStatus.Ok, (await harness.WindowAsync(row.Id, Out)).Status);
        var readsBefore = harness.Store.Reads.Count;
        await harness.CorruptAsync(
            $"UPDATE verification_executions SET Arguments = '[\"--check\",\"99\"]' WHERE Id = {Op(row.OperationId!.Value)}");

        var window = await harness.WindowAsync(row.Id, Out);

        AssertRefused(window, LocalExecutionVerificationOutputWindowStatus.Unavailable, 0);
        Assert.Null(window.ExecutionId);
        Assert.Equal(readsBefore, harness.Store.Reads.Count);
    }

    [Fact]
    public async Task Ambiguous_rows_for_the_requested_stream_are_unavailable_and_the_sibling_stream_stays_readable()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        await harness.CorruptAsync(
            "DROP INDEX IF EXISTS IX_verification_output_artifacts_VerificationExecutionId_Purpose;"
            + "INSERT INTO verification_output_artifacts (Id, VerificationExecutionId, Purpose, RelativeStoragePath, ContentHash, ByteLength, "
            + "Truncated, CaptureOutcome, CreatedAtUtc) SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4000-8000-' || "
            + "lower(hex(randomblob(6))), VerificationExecutionId, Purpose, RelativeStoragePath, ContentHash, ByteLength, Truncated, "
            + $"CaptureOutcome, CreatedAtUtc FROM verification_output_artifacts WHERE VerificationExecutionId = {Op(row.OperationId!.Value)} "
            + "AND Purpose = 'StandardOutput';");

        var ambiguous = await harness.WindowAsync(row.Id, Out);
        var sibling = await harness.WindowAsync(row.Id, Err);

        AssertRefused(ambiguous, LocalExecutionVerificationOutputWindowStatus.Unavailable, 0);
        Assert.Equal(LocalExecutionVerificationOutputWindowStatus.Ok, sibling.Status);
    }

    [Fact]
    public async Task A_recovered_capture_keeps_truncation_unknown_and_a_window_reads_the_same_after_newer_recipe_edits_and_runs()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        await harness.CorruptAsync(
            "UPDATE verification_output_artifacts SET CaptureOutcome = 'RecoveredAfterHostInterruption', Truncated = NULL "
            + $"WHERE VerificationExecutionId = {Op(row.OperationId!.Value)} AND Purpose = 'StandardError'");
        var recipes = await harness.Scene.ReadRecipesAsync();
        await harness.Scene.EditRecipeAsync(recipes[0].Id, executablePath: @"C:\tools\renamed.exe", timeoutSeconds: 60);
        await LocalExecutionSequenceTestScene.CreateAsync(fixture);

        var window = await harness.WindowAsync(row.Id, Err);

        Assert.Equal(LocalExecutionVerificationOutputWindowStatus.Ok, window.Status);
        Assert.Equal(VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption, window.Artifact!.CaptureOutcome);
        Assert.Null(window.Artifact.Truncated);
        Assert.Equal("stderr failure text", window.Text);
    }

    [Fact]
    public async Task A_window_changes_nothing_executes_only_selects_and_selects_no_executable_argument_of_another_recipe()
    {
        var (harness, _) = await CompletedAsync();
        var row = await harness.RowAsync(1);
        var before = await harness.ChangeFingerprintAsync();
        var log = new StatementLog();

        var window = await harness.WindowAsync(row.Id, Out, 0, 64, log);

        Assert.Equal(LocalExecutionVerificationOutputWindowStatus.Ok, window.Status);
        Assert.Equal(before, await harness.ChangeFingerprintAsync());
        log.AssertReadOnly();
        log.AssertNeverSelects("\"PreparationOwnerToken\"", "\"SettingsSnapshot\"", "\"WorkspacePath\"", "\"StructuredContentJson\"");
        Assert.Single(harness.Store.Reads);
    }
}
