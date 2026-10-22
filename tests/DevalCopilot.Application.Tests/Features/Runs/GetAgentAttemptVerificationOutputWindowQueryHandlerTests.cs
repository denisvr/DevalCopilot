using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationOutputWindow;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0052 sealed windows at the query boundary: every window re-proves the whole membership and the execution's ownership over real
/// file-backed SQLite, resolves only the requested stream's row and hands the admitted values unchanged to the artifact store's sealed
/// verification. Feature tests use the real store over disposable owned files (exact delegation, empty, missing, tampered and multiwindow
/// UTF-8 text) and a recording wrapper that proves which calls reach the store at all. The store's own containment proofs are reused
/// unchanged.
/// </summary>
public sealed class GetAgentAttemptVerificationOutputWindowQueryHandlerTests : IAsyncLifetime
{
    private const string Secret = "SECRET-SENTINEL-52b9";

    private readonly SqliteDatabaseFixture _fixture = new();
    private readonly RecordedVerificationSeed _seed;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-verification-window-{Guid.NewGuid():N}");

    public GetAgentAttemptVerificationOutputWindowQueryHandlerTests() => _seed = new RecordedVerificationSeed(_fixture);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record StoreCall(string Path, long Length, string Hash, long Offset, int MaxBytes);

    private sealed class RecordingStore(IArtifactStore inner) : IArtifactStore
    {
        public List<StoreCall> Calls { get; } = [];

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken)
        {
            Calls.Add(new StoreCall(relativeStoragePath, expectedByteLength, expectedContentHash, fromOffset, maxBytes));
            return inner.VerifyAndReadSealedAsync(
                relativeStoragePath, expectedByteLength, expectedContentHash, fromOffset, maxBytes, cancellationToken);
        }

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public Task<SealedOutputFile?> DescribeSealedFileAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingStore(Exception exception) : IArtifactStore
    {
        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken) => throw exception;

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public Task<SealedOutputFile?> DescribeSealedFileAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CommandRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FaultingInterceptor(string table) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"FROM \"{table}\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("injected database fault");
            }

            return ValueTask.FromResult(result);
        }
    }

    private static string HashOf(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string StreamName(VerificationOutputPurpose stream) => stream == VerificationOutputPurpose.StandardOutput ? "stdout" : "stderr";

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
        var row = await _seed.AddOutputAsync(executionId, stream, relative, hash, bytes.Length, truncated, capture);
        return (relative, row, hash);
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private async Task<Result<GetAgentAttemptVerificationOutputWindowQueryResult>> SendAsync(
        SeededVerificationScene scene,
        Guid executionId,
        VerificationOutputPurpose stream,
        long offset,
        int maxBytes,
        IArtifactStore? store = null,
        CancellationToken cancellationToken = default,
        params IInterceptor[] interceptors)
    {
        await using var dbContext = _fixture.CreateContext(interceptors);
        return await new GetAgentAttemptVerificationOutputWindowQueryHandler(dbContext, store ?? new FilesystemArtifactStore(_root))
            .HandleAsync(
                new GetAgentAttemptVerificationOutputWindowQuery(scene.RunId, scene.Attempt.Id, executionId, stream, offset, maxBytes),
                cancellationToken);
    }

    private async Task<GetAgentAttemptVerificationOutputWindowQueryResult> ReadAsync(
        SeededVerificationScene scene, Guid executionId, VerificationOutputPurpose stream, long offset = 0, int maxBytes = 16384,
        IArtifactStore? store = null)
    {
        var result = await SendAsync(scene, executionId, stream, offset, maxBytes, store);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static void AssertRefused(
        GetAgentAttemptVerificationOutputWindowQueryResult result, AgentAttemptVerificationOutputWindowStatus status, long offset)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Artifact);
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(offset, result.NextOffset);
        Assert.Equal(0, result.TotalLength);
    }

    private const VerificationOutputPurpose Out = VerificationOutputPurpose.StandardOutput;
    private const VerificationOutputPurpose Err = VerificationOutputPurpose.StandardError;

    [Fact]
    public async Task The_admitted_row_reaches_the_store_unchanged_and_only_the_requested_stream_is_read()
    {
        var scene = await _seed.SeedAsync(true, true, false);
        var execution = scene.Members[0].Execution.Id;
        var bytes = Encoding.UTF8.GetBytes("first line\nsecond line\n");
        var stdout = await PrepareAsync(execution, Out, bytes);
        await PrepareAsync(execution, Err, Encoding.UTF8.GetBytes("error stream bytes"));
        var store = new RecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(scene, execution, Out, offset: 6, maxBytes: 64, store);

        var call = Assert.Single(store.Calls);
        Assert.Equal(new StoreCall(stdout.Path, bytes.Length, stdout.Hash, 6, 64), call);
        Assert.Equal(AgentAttemptVerificationOutputWindowStatus.Ok, result.Status);
        Assert.Equal(scene.Attempt.Id, result.AttemptId);
        Assert.Equal(execution, result.ExecutionId);
        Assert.Equal(Out, result.Stream);
        Assert.Equal("line\nsecond line\n", result.Text);
        Assert.Equal(bytes.Length, result.NextOffset);
        Assert.Equal(bytes.Length, result.TotalLength);
        Assert.Equal(
            new RecordedVerificationOutputDescriptor(
                RecordedVerificationOutputDescriptorStatus.Recorded, stdout.Row.Id, bytes.Length,
                VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, false),
            result.Artifact);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-int.MaxValue)]
    [InlineData(-int.MaxValue + 1)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MaxValue - 1)]
    [InlineData(int.MaxValue)]
    public async Task The_stream_of_a_failed_diagnosis_member_is_served_for_every_int32_exit_code_boundary(int exitCode)
    {
        var scene = await _seed.SeedAsync(true, true, false);
        var execution = scene.Members[0].Execution.Id;
        await ExecuteAsync($"UPDATE verification_executions SET ExitCode = {exitCode} WHERE Id = {execution}");
        var bytes = Encoding.UTF8.GetBytes("failure detail\n");
        var stdout = await PrepareAsync(execution, Out, bytes);
        var store = new RecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(scene, execution, Out, store: store);

        Assert.Equal(new StoreCall(stdout.Path, bytes.Length, stdout.Hash, 0, 16384), Assert.Single(store.Calls));
        Assert.Equal(AgentAttemptVerificationOutputWindowStatus.Ok, result.Status);
        Assert.Equal("failure detail\n", result.Text);
        Assert.Equal(bytes.Length, result.NextOffset);
        Assert.Equal(bytes.Length, result.TotalLength);
    }

    [Theory]
    [InlineData("real-beside-negated-maximum")]
    [InlineData("real-below-negated-maximum")]
    [InlineData("above-int32-maximum")]
    [InlineData("below-int32-minimum")]
    [InlineData("text")]
    [InlineData("blob")]
    public async Task A_failed_member_whose_exit_code_storage_is_not_an_int32_makes_the_window_unavailable_before_any_file_read(string defect)
    {
        var scene = await _seed.SeedAsync(true, true, false);
        var execution = scene.Members[0].Execution.Id;
        await PrepareAsync(execution, Out, Encoding.UTF8.GetBytes("failure detail\n"));
        var store = new RecordingStore(new FilesystemArtifactStore(_root));
        FormattableString update = defect switch
        {
            "real-beside-negated-maximum" => $"UPDATE verification_executions SET ExitCode = -2147483646.5 WHERE Id = {execution}",
            "real-below-negated-maximum" => $"UPDATE verification_executions SET ExitCode = -2147483647.5 WHERE Id = {execution}",
            "above-int32-maximum" => $"UPDATE verification_executions SET ExitCode = 2147483648 WHERE Id = {execution}",
            "below-int32-minimum" => $"UPDATE verification_executions SET ExitCode = -2147483649 WHERE Id = {execution}",
            "text" => $"UPDATE verification_executions SET ExitCode = {Secret} WHERE Id = {execution}",
            "blob" => $"UPDATE verification_executions SET ExitCode = X'0102' WHERE Id = {execution}",
            _ => throw new InvalidOperationException(defect),
        };
        await ExecuteAsync(update);

        AssertRefused(await ReadAsync(scene, execution, Out, offset: 3, store: store), AgentAttemptVerificationOutputWindowStatus.Unavailable, 3);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task The_standard_error_stream_of_a_passed_review_member_is_served_and_a_recovered_capture_keeps_truncation_unknown()
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        var bytes = Encoding.UTF8.GetBytes("warnings\n");
        await PrepareAsync(execution, Err, bytes, truncated: null, VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption);

        var result = await ReadAsync(scene, execution, Err);

        Assert.Equal(AgentAttemptVerificationOutputWindowStatus.Ok, result.Status);
        Assert.Equal("warnings\n", result.Text);
        Assert.Equal(VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption, result.Artifact!.CaptureOutcome);
        Assert.Null(result.Artifact.Truncated);
    }

    [Fact]
    public async Task A_verified_empty_stream_is_ok_with_empty_text_at_offset_zero_and_a_read_exactly_at_the_end_is_empty()
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        await PrepareAsync(execution, Out, []);
        await PrepareAsync(execution, Err, Encoding.UTF8.GetBytes("abc"));

        var empty = await ReadAsync(scene, execution, Out);
        Assert.Equal(AgentAttemptVerificationOutputWindowStatus.Ok, empty.Status);
        Assert.Equal(string.Empty, empty.Text);
        Assert.Equal(0, empty.NextOffset);
        Assert.Equal(0, empty.TotalLength);
        Assert.Equal(0, empty.Artifact!.ByteLength);

        var atEnd = await ReadAsync(scene, execution, Err, offset: 3);
        Assert.Equal(AgentAttemptVerificationOutputWindowStatus.Ok, atEnd.Status);
        Assert.Equal(string.Empty, atEnd.Text);
        Assert.Equal(3, atEnd.NextOffset);
        Assert.Equal(3, atEnd.TotalLength);
    }

    [Fact]
    public async Task A_missing_file_and_a_tampered_file_are_distinct_and_disclose_no_length_or_text()
    {
        var scene = await _seed.SeedAsync(true, true, true);
        var missing = scene.Members[0].Execution.Id;
        var tampered = scene.Members[1].Execution.Id;
        var sameLength = scene.Members[1].Execution.Id;
        var missingFile = await PrepareAsync(missing, Out, Encoding.UTF8.GetBytes("will be removed"));
        File.Delete(Path.Combine(_root, missingFile.Path));
        var tamperedFile = await PrepareAsync(tampered, Out, Encoding.UTF8.GetBytes("original content"));
        await File.WriteAllBytesAsync(Path.Combine(_root, tamperedFile.Path), Encoding.UTF8.GetBytes("Original content"));
        var lengthChanged = await PrepareAsync(sameLength, Err, Encoding.UTF8.GetBytes("original content"));
        await File.WriteAllBytesAsync(Path.Combine(_root, lengthChanged.Path), Encoding.UTF8.GetBytes(Secret + " changed length"));

        AssertRefused(await ReadAsync(scene, missing, Out, offset: 4), AgentAttemptVerificationOutputWindowStatus.Missing, 4);
        AssertRefused(await ReadAsync(scene, tampered, Out, offset: 2), AgentAttemptVerificationOutputWindowStatus.IntegrityMismatch, 2);
        var changed = await ReadAsync(scene, sameLength, Err);
        AssertRefused(changed, AgentAttemptVerificationOutputWindowStatus.IntegrityMismatch, 0);
        Assert.DoesNotContain(Secret, System.Text.Json.JsonSerializer.Serialize(changed), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multibyte_utf8_text_is_returned_whole_across_windows_without_splitting_a_code_point()
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        var original = string.Concat(Enumerable.Repeat("é ✓ 𝄞 日本語 – ", 40)) + "end";
        var bytes = Encoding.UTF8.GetBytes(original);
        await PrepareAsync(execution, Out, bytes);

        var builder = new StringBuilder();
        long offset = 0;
        var windows = 0;
        while (true)
        {
            var window = await ReadAsync(scene, execution, Out, offset, maxBytes: 64);
            Assert.Equal(AgentAttemptVerificationOutputWindowStatus.Ok, window.Status);
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
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        await PrepareAsync(execution, Err, Encoding.UTF8.GetBytes("only stderr"));
        var store = new RecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(scene, execution, Out, offset: 9, store: store);

        AssertRefused(result, AgentAttemptVerificationOutputWindowStatus.NotRecorded, 9);
        Assert.Empty(store.Calls);
    }

    [Theory]
    [InlineData("hash-uppercase")]
    [InlineData("hash-no-prefix")]
    [InlineData("hash-short")]
    [InlineData("hash-long")]
    [InlineData("hash-nonhex")]
    [InlineData("hash-oversized")]
    [InlineData("path-blank")]
    [InlineData("path-over-1024-units")]
    [InlineData("path-over-3072-bytes")]
    [InlineData("path-rooted")]
    [InlineData("path-nul-hidden")]
    [InlineData("capture-unknown")]
    [InlineData("length-negative")]
    [InlineData("length-text")]
    [InlineData("truncated-two")]
    [InlineData("truncated-negative-one")]
    [InlineData("truncated-seven")]
    [InlineData("truncated-real")]
    [InlineData("truncated-text")]
    [InlineData("truncated-blob")]
    [InlineData("truncated-oversized")]
    public async Task Invalid_or_oversized_storage_is_unavailable_before_any_file_read(string defect)
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        var stored = await PrepareAsync(execution, Out, Encoding.UTF8.GetBytes("content"));
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
            "truncated-two" => $"UPDATE verification_output_artifacts SET Truncated = 2 WHERE Id = {id}",
            "truncated-negative-one" => $"UPDATE verification_output_artifacts SET Truncated = -1 WHERE Id = {id}",
            "truncated-seven" => $"UPDATE verification_output_artifacts SET Truncated = 7 WHERE Id = {id}",
            "truncated-real" => $"UPDATE verification_output_artifacts SET Truncated = 1.5 WHERE Id = {id}",
            "truncated-text" => $"UPDATE verification_output_artifacts SET Truncated = {Secret} WHERE Id = {id}",
            "truncated-blob" => $"UPDATE verification_output_artifacts SET Truncated = X'0102' WHERE Id = {id}",
            "truncated-oversized" => $"UPDATE verification_output_artifacts SET Truncated = {new string('1', 300)} WHERE Id = {id}",
            _ => throw new InvalidOperationException(defect),
        };
        await ExecuteAsync(update);
        var store = new RecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(scene, execution, Out, offset: 1, store: store);

        AssertRefused(result, AgentAttemptVerificationOutputWindowStatus.Unavailable, 1);
        Assert.Empty(store.Calls);
        Assert.DoesNotContain(Secret, System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_offset_past_the_recorded_length_is_a_validation_refusal_and_the_file_is_never_opened()
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        await PrepareAsync(execution, Out, Encoding.UTF8.GetBytes("12345"));
        var store = new RecordingStore(new FilesystemArtifactStore(_root));

        var result = await SendAsync(scene, execution, Out, 6, 64, store);

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(Assert.Single(result.Errors));
        Assert.Equal(GetAgentAttemptVerificationOutputWindowErrors.InvalidOffsetCode, error.Code);
        Assert.Empty(store.Calls);
        Assert.DoesNotContain("6", error.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1L, 64)]
    [InlineData(9007199254740992L, 64)]
    [InlineData(long.MaxValue, 64)]
    [InlineData(0L, 63)]
    [InlineData(0L, 65537)]
    [InlineData(0L, 0)]
    [InlineData(0L, -5)]
    [InlineData(0L, int.MaxValue)]
    public async Task An_offset_or_cap_outside_its_bounds_is_refused_not_clamped_before_any_database_or_store_access(long offset, int maxBytes)
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        await PrepareAsync(execution, Out, Encoding.UTF8.GetBytes("content"));
        var store = new RecordingStore(new FilesystemArtifactStore(_root));
        var recorder = new CommandRecorder();

        var result = await SendAsync(scene, execution, Out, offset, maxBytes, store, default, recorder);

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(Assert.Single(result.Errors));
        Assert.Contains(error.Code, new[]
        {
            GetAgentAttemptVerificationOutputWindowErrors.InvalidOffsetCode, GetAgentAttemptVerificationOutputWindowErrors.InvalidMaxBytesCode,
        });
        Assert.Empty(store.Calls);
        Assert.Empty(recorder.Commands);
    }

    [Fact]
    public async Task The_smallest_and_largest_caps_and_the_largest_safe_offset_are_admitted_by_the_bounds()
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        var bytes = Encoding.UTF8.GetBytes(new string('x', 70_000));
        await PrepareAsync(execution, Out, bytes);
        var store = new RecordingStore(new FilesystemArtifactStore(_root));

        var smallest = await ReadAsync(scene, execution, Out, 0, 64, store);
        var largest = await ReadAsync(scene, execution, Out, 0, 65536, store);

        Assert.Equal(64, smallest.NextOffset);
        Assert.Equal(65536, largest.NextOffset);
        Assert.Equal([64, 65536], store.Calls.Select(call => call.MaxBytes));

        var beyond = await SendAsync(scene, execution, Out, 9007199254740991L, 64, store);
        Assert.True(beyond.IsFailure);
        Assert.Equal(GetAgentAttemptVerificationOutputWindowErrors.InvalidOffsetCode, Assert.Single(beyond.Errors).Code);
        Assert.Equal(2, store.Calls.Count);
    }

    [Fact]
    public async Task A_nonmember_execution_an_unknown_run_and_a_foreign_attempt_are_the_same_not_found_and_read_no_file()
    {
        var scene = await _seed.SeedAsync(false, false);
        var other = await _seed.SeedAsync(false, false);
        await PrepareAsync(other.Members[0].Execution.Id, Out, Encoding.UTF8.GetBytes("foreign"));
        var store = new RecordingStore(new FilesystemArtifactStore(_root));

        var results = new[]
        {
            await SendAsync(scene, other.Members[0].Execution.Id, Out, 0, 64, store),
            await SendAsync(scene, Guid.NewGuid(), Out, 0, 64, store),
            await SendAsync(scene with { RunId = Guid.NewGuid() }, scene.Members[0].Execution.Id, Out, 0, 64, store),
            await SendAsync(scene with { RunId = other.RunId }, scene.Members[0].Execution.Id, Out, 0, 64, store),
        };

        foreach (var result in results)
        {
            Assert.True(result.IsFailure);
            Assert.Equal(AgentAttemptVerificationErrors.NotFoundCode, Assert.Single(result.Errors).Code);
        }

        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_defect_in_any_other_member_makes_the_window_unavailable_without_reading_the_requested_stream()
    {
        var scene = await _seed.SeedAsync(true, true, false, false);
        var requested = scene.Members[0].Execution.Id;
        await PrepareAsync(requested, Out, Encoding.UTF8.GetBytes("would be served"));
        await ExecuteAsync($"UPDATE verification_executions SET Status = 'TimedOut' WHERE Id = {scene.Members[^1].Execution.Id}");
        var store = new RecordingStore(new FilesystemArtifactStore(_root));

        var result = await ReadAsync(scene, requested, Out, store: store);

        AssertRefused(result, AgentAttemptVerificationOutputWindowStatus.Unavailable, 0);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task Membership_is_reproved_for_each_window_so_a_later_defect_stops_the_next_window()
    {
        var scene = await _seed.SeedAsync(false, false, false);
        var execution = scene.Members[0].Execution.Id;
        await PrepareAsync(execution, Out, Encoding.UTF8.GetBytes(new string('y', 300)));

        var first = await ReadAsync(scene, execution, Out, 0, 64);
        Assert.Equal(AgentAttemptVerificationOutputWindowStatus.Ok, first.Status);

        await ExecuteAsync($"UPDATE git_checkpoints SET FingerprintSha256 = {new string('b', 64)} WHERE Id = {scene.Checkpoint.Id}");

        AssertRefused(await ReadAsync(scene, execution, Out, first.NextOffset, 64), AgentAttemptVerificationOutputWindowStatus.Unavailable, first.NextOffset);
    }

    [Fact]
    public async Task Only_the_requested_row_and_ownership_scalars_are_read_and_nothing_is_written()
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        await PrepareAsync(execution, Out, Encoding.UTF8.GetBytes("content"));
        await PrepareAsync(execution, Err, Encoding.UTF8.GetBytes("other"));
        var recorder = new CommandRecorder();

        await SendAsync(scene, execution, Out, 0, 64, null, default, recorder);

        Assert.All(recorder.Commands, command =>
        {
            Assert.StartsWith("SELECT", command.TrimStart(), StringComparison.Ordinal);
            foreach (var forbidden in new[] { "INSERT", "UPDATE ", "DELETE", "SnapshotSha256", "WorkspacePath", "ExecutablePath", "\"Arguments\"", "verification_commands" })
            {
                Assert.DoesNotContain(forbidden, command, StringComparison.Ordinal);
            }
        });
        var outputs = Assert.Single(recorder.Commands, command => command.Contains("FROM \"verification_output_artifacts\"", StringComparison.Ordinal));
        Assert.Contains("\"Purpose\" =", outputs, StringComparison.Ordinal);
        Assert.Contains("LIMIT", outputs, StringComparison.Ordinal);
        Assert.Equal(6, recorder.Commands.Count);
    }

    [Theory]
    [InlineData("attempts")]
    [InlineData("git_checkpoints")]
    [InlineData("attempt_verification_evidence")]
    [InlineData("verification_executions")]
    [InlineData("verification_output_artifacts")]
    public async Task A_database_fault_propagates_and_is_never_an_availability_state(string table)
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        await PrepareAsync(execution, Out, Encoding.UTF8.GetBytes("content"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(scene, execution, Out, 0, 64, null, default, new FaultingInterceptor(table)));
    }

    [Fact]
    public async Task A_store_fault_and_cancellation_propagate_without_a_catch_all()
    {
        var scene = await _seed.SeedAsync(false, false);
        var execution = scene.Members[0].Execution.Id;
        await PrepareAsync(execution, Out, Encoding.UTF8.GetBytes("content"));

        await Assert.ThrowsAsync<IOException>(
            () => SendAsync(scene, execution, Out, 0, 64, new ThrowingStore(new IOException("disk fault"))));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SendAsync(scene, execution, Out, 0, 64, null, cancelled.Token));
    }
}
