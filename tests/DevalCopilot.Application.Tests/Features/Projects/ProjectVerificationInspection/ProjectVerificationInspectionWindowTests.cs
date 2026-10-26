using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationOutputWindow;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.Processes;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>
/// ADR-0069 sealed windows at the query boundary: every window re-proves the selected execution over real file-backed SQLite, resolves only
/// the requested stream's row and hands the admitted values unchanged to the artifact store's sealed verification, which these tests use
/// over disposable owned files (exact delegation, empty, missing, tampered and multiwindow UTF-8 text) beside a recording wrapper that
/// proves which calls reach the store at all. No Agent attempt or local sequence exists for any execution read here.
/// </summary>
public sealed class ProjectVerificationInspectionWindowTests : ProjectVerificationInspectionTestBase
{
    private const string Secret = "SECRET-SENTINEL-69a1";
    private const VerificationOutputPurpose Out = VerificationOutputPurpose.StandardOutput;
    private const VerificationOutputPurpose Err = VerificationOutputPurpose.StandardError;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-project-window-{Guid.NewGuid():N}");

    public override async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FaultingInterceptor(string table) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"\"{table}\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("injected database fault");
            }

            return ValueTask.FromResult(result);
        }
    }

    private static string HashOf(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string StreamName(VerificationOutputPurpose stream) => stream == Out ? "stdout" : "stderr";

    private async Task<(Guid ProjectId, SeededExecution Execution)> SeedExecutionAsync(
        ExecutionShape shape = ExecutionShape.FailedExited, int number = 1)
    {
        var projectId = await Seed.SeedProjectAsync();
        var (workspace, checkpoint) = await Seed.SeedWorkspaceAsync(projectId);
        var command = await Seed.SeedCommandAsync(projectId);
        return (projectId, await Seed.SeedExecutionAsync(projectId, workspace, checkpoint, command, number, shape));
    }

    /// <summary>Writes the sealed bytes under the disposable root and records the stream's row exactly as the real recorder would.</summary>
    private async Task<(string Path, VerificationOutputArtifact Row, string Hash)> PrepareAsync(
        Guid executionId, VerificationOutputPurpose stream, byte[] bytes, bool? truncated = false,
        VerificationOutputCaptureOutcome capture = VerificationOutputCaptureOutcome.CapturedWithKnownTruncation)
    {
        var relative = Path.Combine("verification", executionId.ToString("N"), StreamName(stream) + ".sealed");
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes);
        var hash = HashOf(bytes);
        var row = await Seed.AddOutputAsync(executionId, stream, relative, hash, bytes.Length, truncated, capture);
        return (relative, row, hash);
    }

    private async Task<Result<GetProjectVerificationOutputWindowQueryResult>> SendAsync(
        Guid projectId,
        Guid executionId,
        VerificationOutputPurpose stream,
        long offset,
        int maxBytes,
        IArtifactStore? store = null,
        CancellationToken cancellationToken = default,
        params IInterceptor[] interceptors)
    {
        await using var dbContext = Fixture.CreateContext(interceptors);
        var result = await new GetProjectVerificationOutputWindowQueryHandler(dbContext, store ?? new FilesystemArtifactStore(_root))
            .HandleAsync(
                new GetProjectVerificationOutputWindowQuery(projectId, executionId, stream, offset, maxBytes), cancellationToken);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        return result;
    }

    private async Task<GetProjectVerificationOutputWindowQueryResult> ReadAsync(
        Guid projectId, Guid executionId, VerificationOutputPurpose stream, long offset = 0, int maxBytes = 16384, IArtifactStore? store = null)
    {
        var result = await SendAsync(projectId, executionId, stream, offset, maxBytes, store);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static void AssertRefused(GetProjectVerificationOutputWindowQueryResult result, ProjectVerificationOutputWindowStatus status, long offset)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Artifact);
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(offset, result.NextOffset);
        Assert.Equal(0, result.TotalLength);
    }

    [Fact]
    public async Task The_admitted_row_reaches_the_store_unchanged_and_only_the_requested_stream_is_read()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        var stdout = await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("standard output text"));
        await PrepareAsync(execution.Id, Err, Encoding.UTF8.GetBytes("standard error text"));
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(projectId, execution.Id, Out, offset: 3, maxBytes: 64, store: store);

        Assert.Equal(ProjectVerificationOutputWindowStatus.Ok, result.Status);
        Assert.Equal((projectId, execution.Id, Out), (result.ProjectId, result.ExecutionId, result.Stream));
        Assert.Equal("ndard output text", result.Text);
        Assert.Equal(20, result.NextOffset);
        Assert.Equal(20, result.TotalLength);
        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stdout.Row.Id, 20, VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, false),
            result.Artifact);
        Assert.Equal(new StoreCall(stdout.Path, 20, stdout.Hash, 3, 64), Assert.Single(store.Calls));
    }

    [Fact]
    public async Task The_standard_error_stream_of_a_recovered_capture_keeps_truncation_unknown_and_the_other_stream_is_untouched()
    {
        var (projectId, execution) = await SeedExecutionAsync(ExecutionShape.Interrupted);
        await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("out"));
        var stderr = await PrepareAsync(
            execution.Id, Err, Encoding.UTF8.GetBytes("recovered text"), null, VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption);

        var result = await ReadAsync(projectId, execution.Id, Err);

        Assert.Equal(ProjectVerificationOutputWindowStatus.Ok, result.Status);
        Assert.Equal("recovered text", result.Text);
        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stderr.Row.Id, 14, VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption, null),
            result.Artifact);
    }

    [Fact]
    public async Task A_verified_empty_stream_is_ok_with_empty_text_and_a_read_exactly_at_the_end_is_empty()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        await PrepareAsync(execution.Id, Out, []);
        await PrepareAsync(execution.Id, Err, Encoding.UTF8.GetBytes("abc"));

        var empty = await ReadAsync(projectId, execution.Id, Out);
        var atEnd = await ReadAsync(projectId, execution.Id, Err, offset: 3);

        Assert.Equal((ProjectVerificationOutputWindowStatus.Ok, string.Empty, 0L, 0L), (empty.Status, empty.Text, empty.NextOffset, empty.TotalLength));
        Assert.Equal((ProjectVerificationOutputWindowStatus.Ok, string.Empty, 3L, 3L), (atEnd.Status, atEnd.Text, atEnd.NextOffset, atEnd.TotalLength));
    }

    [Fact]
    public async Task A_missing_file_and_a_tampered_file_are_distinct_and_disclose_no_length_or_text()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        var missing = await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("will be removed"));
        File.Delete(Path.Combine(_root, missing.Path));
        var tampered = await PrepareAsync(execution.Id, Err, Encoding.UTF8.GetBytes("original content"));
        await File.WriteAllBytesAsync(Path.Combine(_root, tampered.Path), Encoding.UTF8.GetBytes("Original content"));

        AssertRefused(await ReadAsync(projectId, execution.Id, Out, offset: 4), ProjectVerificationOutputWindowStatus.Missing, 4);
        var changed = await ReadAsync(projectId, execution.Id, Err, offset: 2);
        AssertRefused(changed, ProjectVerificationOutputWindowStatus.IntegrityMismatch, 2);
        await File.WriteAllBytesAsync(Path.Combine(_root, tampered.Path), Encoding.UTF8.GetBytes(Secret + " changed length"));
        var longer = await ReadAsync(projectId, execution.Id, Err);
        AssertRefused(longer, ProjectVerificationOutputWindowStatus.IntegrityMismatch, 0);
        Assert.DoesNotContain(Secret, System.Text.Json.JsonSerializer.Serialize(longer), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multibyte_utf8_text_is_returned_whole_across_windows_without_splitting_a_code_point()
    {
        var (projectId, execution) = await SeedExecutionAsync(ExecutionShape.Passed);
        var original = string.Concat(Enumerable.Repeat("é ✓ 𝄞 日本語 – ", 40)) + "end";
        var bytes = Encoding.UTF8.GetBytes(original);
        await PrepareAsync(execution.Id, Out, bytes);

        var builder = new StringBuilder();
        long offset = 0;
        var windows = 0;
        while (true)
        {
            var window = await ReadAsync(projectId, execution.Id, Out, offset, maxBytes: 64);
            Assert.Equal(ProjectVerificationOutputWindowStatus.Ok, window.Status);
            Assert.Equal(bytes.Length, window.TotalLength);
            Assert.InRange(window.NextOffset - offset, 0, 64);
            Assert.DoesNotContain('�', window.Text);
            builder.Append(window.Text);
            windows++;
            if (window.NextOffset == window.TotalLength)
            {
                break;
            }

            Assert.True(window.NextOffset > offset, "A window with bytes remaining must make progress.");
            offset = window.NextOffset;
        }

        Assert.True(windows > 5);
        Assert.Equal(original, builder.ToString());
    }

    [Fact]
    public async Task A_stream_with_no_row_is_not_recorded_and_never_reaches_the_store()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        await PrepareAsync(execution.Id, Err, Encoding.UTF8.GetBytes("only stderr"));
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(projectId, execution.Id, Out, offset: 9, store: store);

        AssertRefused(result, ProjectVerificationOutputWindowStatus.NotRecorded, 9);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_running_execution_has_no_sealed_stream_so_no_live_partial_output_is_ever_served()
    {
        var (projectId, execution) = await SeedExecutionAsync(ExecutionShape.RunningDispatched);
        var partial = Path.Combine(_root, "partial.log");
        await File.WriteAllTextAsync(partial, "live partial output");
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(projectId, execution.Id, Out, store: store);

        AssertRefused(result, ProjectVerificationOutputWindowStatus.NotRecorded, 0);
        Assert.Empty(store.Calls);
    }

    public static TheoryData<string> StorageDefectNames() => new(
        "hash-uppercase", "hash-no-prefix", "hash-short", "hash-long", "hash-nonhex", "hash-oversized", "path-blank", "path-over-1024-units",
        "path-over-3072-bytes", "path-rooted", "path-nul-hidden", "capture-unknown", "length-negative", "length-text", "length-real",
        "truncated-two", "truncated-seven", "truncated-real", "truncated-text", "truncated-blob", "truncated-oversized", "artifact-id-garbage",
        "artifact-id-empty", "execution-id-lowercase-reads-as-absent-row");

    [Theory]
    [MemberData(nameof(StorageDefectNames))]
    public async Task Invalid_or_oversized_storage_is_refused_before_any_file_read(string defect)
    {
        var (projectId, execution) = await SeedExecutionAsync();
        var stored = await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("content"));
        var id = stored.Row.Id;
        var valid = stored.Hash["sha256:".Length..];
        FormattableString update = defect switch
        {
            "hash-uppercase" => $"UPDATE verification_output_artifacts SET ContentHash = {"sha256:" + valid.ToUpperInvariant()} WHERE Id = {id}",
            "hash-no-prefix" => $"UPDATE verification_output_artifacts SET ContentHash = {valid + "0000000"} WHERE Id = {id}",
            "hash-short" => $"UPDATE verification_output_artifacts SET ContentHash = {"sha256:" + valid[1..]} WHERE Id = {id}",
            "hash-long" => $"UPDATE verification_output_artifacts SET ContentHash = {"sha256:" + valid + "0"} WHERE Id = {id}",
            "hash-nonhex" => $"UPDATE verification_output_artifacts SET ContentHash = {"sha256:" + new string('g', 64)} WHERE Id = {id}",
            "hash-oversized" => $"UPDATE verification_output_artifacts SET ContentHash = 'sha256:' || {valid} || char(0) || {new string('x', 100)} WHERE Id = {id}",
            "path-blank" => $"UPDATE verification_output_artifacts SET RelativeStoragePath = '   ' WHERE Id = {id}",
            "path-over-1024-units" => $"UPDATE verification_output_artifacts SET RelativeStoragePath = {new string('p', 1025)} WHERE Id = {id}",
            "path-over-3072-bytes" => $"UPDATE verification_output_artifacts SET RelativeStoragePath = {new string('p', 3073)} WHERE Id = {id}",
            "path-rooted" => $"UPDATE verification_output_artifacts SET RelativeStoragePath = {Path.Combine(_root, stored.Path)} WHERE Id = {id}",
            "path-nul-hidden" => $"UPDATE verification_output_artifacts SET RelativeStoragePath = 'a' || char(0) || {new string('p', 3100)} WHERE Id = {id}",
            "capture-unknown" => $"UPDATE verification_output_artifacts SET CaptureOutcome = 'Weird' WHERE Id = {id}",
            "length-negative" => $"UPDATE verification_output_artifacts SET ByteLength = -1 WHERE Id = {id}",
            "length-text" => $"UPDATE verification_output_artifacts SET ByteLength = {Secret} WHERE Id = {id}",
            "length-real" => $"UPDATE verification_output_artifacts SET ByteLength = 3.5 WHERE Id = {id}",
            "truncated-two" => $"UPDATE verification_output_artifacts SET Truncated = 2 WHERE Id = {id}",
            "truncated-seven" => $"UPDATE verification_output_artifacts SET Truncated = 7 WHERE Id = {id}",
            "truncated-real" => $"UPDATE verification_output_artifacts SET Truncated = 1.5 WHERE Id = {id}",
            "truncated-text" => $"UPDATE verification_output_artifacts SET Truncated = {Secret} WHERE Id = {id}",
            "truncated-blob" => $"UPDATE verification_output_artifacts SET Truncated = X'0102' WHERE Id = {id}",
            "truncated-oversized" => $"UPDATE verification_output_artifacts SET Truncated = {new string('1', 300)} WHERE Id = {id}",
            "artifact-id-garbage" => $"UPDATE verification_output_artifacts SET Id = 'not-a-guid' WHERE Id = {id}",
            "artifact-id-empty" => $"UPDATE verification_output_artifacts SET Id = {Guid.Empty} WHERE Id = {id}",
            "execution-id-lowercase-reads-as-absent-row" => $"UPDATE verification_output_artifacts SET VerificationExecutionId = lower(VerificationExecutionId) WHERE Id = {id}",
            _ => throw new InvalidOperationException(defect),
        };
        await ExecuteAsync(update);
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(projectId, execution.Id, Out, offset: 1, store: store);

        var expected = defect.StartsWith("execution-id", StringComparison.Ordinal)
            ? ProjectVerificationOutputWindowStatus.NotRecorded
            : ProjectVerificationOutputWindowStatus.Unavailable;
        AssertRefused(result, expected, 1);
        Assert.Empty(store.Calls);
        Assert.DoesNotContain(Secret, System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_row_for_the_same_execution_and_stream_is_unavailable_never_a_choice_between_two()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        var stored = await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("content"));
        await ExecuteAsync($"DROP INDEX IF EXISTS IX_verification_output_artifacts_VerificationExecutionId_Purpose");
        await ExecuteAsync(
            $"INSERT INTO verification_output_artifacts (Id, VerificationExecutionId, Purpose, RelativeStoragePath, ContentHash, ByteLength, Truncated, CaptureOutcome, CreatedAtUtc) SELECT {Guid.NewGuid()}, VerificationExecutionId, Purpose, RelativeStoragePath, ContentHash, ByteLength, Truncated, CaptureOutcome, CreatedAtUtc FROM verification_output_artifacts WHERE Id = {stored.Row.Id}");
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(projectId, execution.Id, Out, store: store);

        AssertRefused(result, ProjectVerificationOutputWindowStatus.Unavailable, 0);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task An_offset_past_the_recorded_length_is_a_validation_refusal_and_the_file_is_never_opened()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("abcdef"));
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));

        var past = await SendAsync(projectId, execution.Id, Out, 7, 64, store);
        var atEnd = await SendAsync(projectId, execution.Id, Out, 6, 64, store);

        Assert.True(past.IsFailure);
        Assert.Equal(ProjectVerificationInspectionErrors.InvalidOffsetCode, Assert.Single(past.Errors).Code);
        Assert.True(atEnd.IsSuccess);
        Assert.Equal(new StoreCall(Path.Combine("verification", execution.Id.ToString("N"), "stdout.sealed"), 6, HashOf("abcdef"u8.ToArray()), 6, 64), Assert.Single(store.Calls));
    }

    [Theory]
    [InlineData(-1L, 64)]
    [InlineData(9_007_199_254_740_992L, 64)]
    [InlineData(long.MaxValue, 64)]
    [InlineData(0L, 63)]
    [InlineData(0L, 65537)]
    [InlineData(0L, 0)]
    [InlineData(0L, -1)]
    [InlineData(0L, int.MaxValue)]
    public async Task An_offset_or_cap_outside_its_bounds_is_refused_not_clamped_before_any_database_or_store_access(long offset, int maxBytes)
    {
        var (projectId, execution) = await SeedExecutionAsync();
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));
        var log = new ProjectVerificationStatementLog();

        var result = await SendAsync(projectId, execution.Id, Out, offset, maxBytes, store, default, log);

        Assert.True(result.IsFailure);
        Assert.Empty(store.Calls);
        Assert.Empty(log.Commands);
        var validator = new GetProjectVerificationOutputWindowQueryValidator();
        Assert.False((await validator.ValidateAsync(new GetProjectVerificationOutputWindowQuery(projectId, execution.Id, Out, offset, maxBytes))).IsValid);
    }

    [Theory]
    [InlineData(0L, 64)]
    [InlineData(9_007_199_254_740_991L, 64)]
    [InlineData(0L, 65536)]
    [InlineData(0L, 16384)]
    public async Task The_smallest_and_largest_caps_and_the_largest_safe_offset_are_admitted_by_the_bounds(long offset, int maxBytes)
    {
        var validator = new GetProjectVerificationOutputWindowQueryValidator();

        var result = await validator.ValidateAsync(new GetProjectVerificationOutputWindowQuery(Guid.NewGuid(), Guid.NewGuid(), Out, offset, maxBytes));

        Assert.True(result.IsValid);
        Assert.False((await validator.ValidateAsync(new GetProjectVerificationOutputWindowQuery(
            Guid.NewGuid(), Guid.NewGuid(), (VerificationOutputPurpose)99, offset, maxBytes))).IsValid);
    }

    [Fact]
    public async Task An_unknown_project_an_absent_execution_a_foreign_execution_and_an_unsupported_stream_are_the_same_not_found_and_read_no_file()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("content"));
        var (otherProject, other) = await SeedExecutionAsync();
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));

        var results = new[]
        {
            await SendAsync(Guid.NewGuid(), execution.Id, Out, 0, 64, store),
            await SendAsync(projectId, Guid.NewGuid(), Out, 0, 64, store),
            await SendAsync(projectId, other.Id, Out, 0, 64, store),
            await SendAsync(otherProject, execution.Id, Out, 0, 64, store),
            await SendAsync(projectId, execution.Id, (VerificationOutputPurpose)99, 0, 64, store),
        };

        Assert.All(results, result =>
        {
            Assert.True(result.IsFailure);
            var error = Assert.Single(result.Errors);
            Assert.Equal(ProjectVerificationInspectionErrors.NotFoundCode, error.Code);
            Assert.Equal(ProjectVerificationInspectionErrors.NotFound().Description, error.Description);
        });
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task An_incoherent_execution_is_unavailable_before_any_stream_row_or_file_is_read_and_a_foreign_one_stays_absent()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("content"));
        await ExecuteAsync($"UPDATE verification_executions SET Status = 'Weird' WHERE Id = {execution.Id}");
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));
        var log = new ProjectVerificationStatementLog();

        var result = await SendAsync(projectId, execution.Id, Out, 5, 64, store, default, log);

        AssertRefused(result.Value, ProjectVerificationOutputWindowStatus.Unavailable, 5);
        Assert.Empty(store.Calls);
        log.AssertNeverSelects("verification_output_artifacts");
    }

    [Fact]
    public async Task The_execution_is_reproved_for_each_window_so_a_later_defect_stops_the_next_window()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes(new string('x', 200)));
        var store = new ProjectVerificationRecordingStore(new FilesystemArtifactStore(_root));

        var first = await ReadAsync(projectId, execution.Id, Out, 0, 64, store);
        await ExecuteAsync($"UPDATE verification_executions SET ClaimedAtUtc = 'garbage' WHERE Id = {execution.Id}");
        var second = await ReadAsync(projectId, execution.Id, Out, first.NextOffset, 64, store);

        Assert.Equal(ProjectVerificationOutputWindowStatus.Ok, first.Status);
        AssertRefused(second, ProjectVerificationOutputWindowStatus.Unavailable, first.NextOffset);
        Assert.Single(store.Calls);
    }

    [Fact]
    public async Task Only_the_proof_scalars_and_the_requested_row_are_read_and_nothing_is_written()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("content"));
        await PrepareAsync(execution.Id, Err, Encoding.UTF8.GetBytes("other"));
        var log = new ProjectVerificationStatementLog();
        var before = await ChangeFingerprintAsync();

        await SendAsync(projectId, execution.Id, Out, 0, 64, null, default, log);

        log.AssertReadOnly();
        log.AssertNeverSelects("WorkspacePath", "ExecutablePath", "\"Arguments\"", "TimeoutSeconds", "verification_commands", "attempts", "runs");
        Assert.Equal(3, log.Commands.Count);
        var outputs = Assert.Single(log.Statements, statement => statement.Contains("FROM \"verification_output_artifacts\"", StringComparison.Ordinal));
        Assert.Contains("\"Purpose\" =", outputs, StringComparison.Ordinal);
        Assert.Contains("LIMIT", outputs, StringComparison.Ordinal);
        Assert.Equal(before, await ChangeFingerprintAsync());
    }

    [Theory]
    [InlineData("verification_executions")]
    [InlineData("git_checkpoints")]
    [InlineData("verification_output_artifacts")]
    public async Task A_database_fault_propagates_and_is_never_an_availability_state(string table)
    {
        var (projectId, execution) = await SeedExecutionAsync();
        await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("content"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(projectId, execution.Id, Out, 0, 64, null, default, new FaultingInterceptor(table)));
    }

    [Fact]
    public async Task A_store_fault_and_cancellation_propagate_without_a_catch_all()
    {
        var (projectId, execution) = await SeedExecutionAsync();
        await PrepareAsync(execution.Id, Out, Encoding.UTF8.GetBytes("content"));

        await Assert.ThrowsAsync<IOException>(
            () => SendAsync(projectId, execution.Id, Out, 0, 64, new ProjectVerificationThrowingStore(new IOException("disk fault"))));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SendAsync(projectId, execution.Id, Out, 0, 64, null, cancelled.Token));
    }
}
