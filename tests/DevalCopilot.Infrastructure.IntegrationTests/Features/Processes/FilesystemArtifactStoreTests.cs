using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Processes;

public sealed class FilesystemArtifactStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-artifact-store-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _store;

    public FilesystemArtifactStoreTests()
    {
        _store = new FilesystemArtifactStore(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (Guid RunId, Guid AttemptId) NewIds() => (Guid.NewGuid(), Guid.NewGuid());

    private async Task WritePartialAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, string content)
    {
        var path = _store.GetPartialPath(runId, attemptId, purpose);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    [Fact]
    public async Task SealAsync_renames_the_partial_file_and_computes_its_length_and_hash()
    {
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, "hello world");

        var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, CancellationToken.None);

        Assert.NotNull(sealedFile);
        Assert.Equal(11, sealedFile.ByteLength);
        Assert.Equal(ExpectedHash("hello world"), sealedFile.ContentHash);
        Assert.False(_store.HasPartialFile(runId, attemptId, ArtifactPurpose.ProcessStandardOutput));
        Assert.True(_store.HasSealedFile(runId, attemptId, ArtifactPurpose.ProcessStandardOutput));
    }

    [Fact]
    public async Task SealAsync_returns_null_when_no_partial_file_exists()
    {
        var (runId, attemptId) = NewIds();

        var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, CancellationToken.None);

        Assert.Null(sealedFile);
    }

    [Fact]
    public async Task DescribeSealedFileAsync_reads_an_already_sealed_file_without_renaming_it_again()
    {
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, "content");
        await _store.SealAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, CancellationToken.None);
        var sealedPath = Path.Combine(_root, _store.GetSealedRelativePath(runId, attemptId, ArtifactPurpose.ProcessStandardOutput));
        var writeTimeBefore = File.GetLastWriteTimeUtc(sealedPath);

        var described = await _store.DescribeSealedFileAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, CancellationToken.None);

        Assert.NotNull(described);
        Assert.Equal(7, described.ByteLength);
        Assert.Equal(ExpectedHash("content"), described.ContentHash);
        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(sealedPath));
    }

    [Fact]
    public async Task VerifyAndReadSealedAsync_serves_bytes_when_length_and_hash_match()
    {
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, "verified content");
        var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, CancellationToken.None);

        var read = await _store.VerifyAndReadSealedAsync(
            sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, 0, 1024, CancellationToken.None);

        Assert.Equal(SealedReadStatus.Ok, read.Status);
        Assert.Equal("verified content", read.Text);
    }

    [Fact]
    public async Task VerifyAndReadSealedAsync_rejects_a_mismatched_length_or_hash()
    {
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, "original content");
        var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, CancellationToken.None);

        // Simulates tampering or bit rot: the file on disk no longer matches its recorded metadata.
        var sealedPath = Path.Combine(_root, sealedFile!.RelativeStoragePath);
        await File.WriteAllTextAsync(sealedPath, "tampered content!!");

        var read = await _store.VerifyAndReadSealedAsync(
            sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, 0, 1024, CancellationToken.None);

        Assert.Equal(SealedReadStatus.IntegrityMismatch, read.Status);
        Assert.Equal(string.Empty, read.Text);
    }

    [Fact]
    public async Task VerifyAndReadSealedAsync_returns_missing_when_the_file_does_not_exist()
    {
        var read = await _store.VerifyAndReadSealedAsync(
            @"runs\nonexistent\attempts\nonexistent\stdout.sealed", 10, "sha256:whatever", 0, 1024, CancellationToken.None);

        Assert.Equal(SealedReadStatus.Missing, read.Status);
    }

    [Theory]
    [InlineData(@"..\..\..\Windows\System32\config\SAM")]
    [InlineData(@"..\outside.txt")]
    [InlineData(@"C:\Windows\System32\config\SAM")]
    [InlineData(@"\\attacker-share\payload")]
    public async Task VerifyAndReadSealedAsync_rejects_a_relative_path_that_would_escape_the_artifact_root(string maliciousPath)
    {
        var read = await _store.VerifyAndReadSealedAsync(maliciousPath, 0, "sha256:anything", 0, 1024, CancellationToken.None);

        Assert.Equal(SealedReadStatus.Missing, read.Status);
    }

    [Fact]
    public async Task ReadPartialAsync_reads_a_bounded_window_from_an_in_progress_file()
    {
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, "0123456789");

        var window = await _store.ReadPartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, 2, 4, CancellationToken.None);

        Assert.Equal("2345", window.Text);
        Assert.Equal(6, window.NextOffset);
        Assert.Equal(10, window.TotalLengthSoFar);
    }

    [Fact]
    public async Task ReadPartialAsync_returns_an_empty_window_when_nothing_has_been_captured_yet()
    {
        var (runId, attemptId) = NewIds();

        var window = await _store.ReadPartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, 0, 1024, CancellationToken.None);

        Assert.Equal(string.Empty, window.Text);
        Assert.Equal(0, window.TotalLengthSoFar);
    }

    [Fact]
    public async Task A_live_reader_of_the_partial_file_does_not_prevent_the_host_from_sealing_it()
    {
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, "still being written");

        // Opens and holds a read handle exactly the way ReadPartialAsync's own sharing mode
        // does, simulating a live-tail poll that is in flight at the moment the host seals.
        var partialPath = _store.GetPartialPath(runId, attemptId, ArtifactPurpose.ProcessStandardOutput);
        await using (new FileStream(partialPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, CancellationToken.None);

            Assert.NotNull(sealedFile);
            Assert.True(_store.HasSealedFile(runId, attemptId, ArtifactPurpose.ProcessStandardOutput));
        }
    }

    [Fact]
    public async Task DeleteOrphanedPartialFile_removes_only_the_partial_file()
    {
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, "orphaned");

        _store.DeleteOrphanedPartialFile(runId, attemptId, ArtifactPurpose.ProcessStandardOutput);

        Assert.False(_store.HasPartialFile(runId, attemptId, ArtifactPurpose.ProcessStandardOutput));
    }

    [Fact]
    public async Task ReadPartialAsync_never_splits_a_multi_byte_codepoint_across_poll_responses()
    {
        // "caf" (3 bytes) + "é" (2 bytes) + " " (1 byte) + "😀" (4 bytes) + " code" (5 bytes) = 15
        // bytes total. maxBytes=7 is deliberately chosen so consecutive windows land exactly
        // one byte into the "é" and one byte into the emoji.
        const string original = "café 😀 code";
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, original);

        var assembled = new System.Text.StringBuilder();
        long offset = 0;
        for (var iterations = 0; iterations < 10 && offset < Encoding.UTF8.GetByteCount(original); iterations++)
        {
            var window = await _store.ReadPartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, offset, 7, CancellationToken.None);
            Assert.DoesNotContain('�', window.Text);
            assembled.Append(window.Text);
            offset = window.NextOffset;
        }

        Assert.Equal(original, assembled.ToString());
    }

    [Fact]
    public async Task VerifyAndReadSealedAsync_never_splits_a_multi_byte_codepoint_across_poll_responses()
    {
        const string original = "café 😀 code";
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, original);
        var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, CancellationToken.None);

        var assembled = new System.Text.StringBuilder();
        long offset = 0;
        for (var iterations = 0; iterations < 10 && offset < sealedFile!.ByteLength; iterations++)
        {
            var window = await _store.VerifyAndReadSealedAsync(
                sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, offset, 7, CancellationToken.None);
            Assert.Equal(SealedReadStatus.Ok, window.Status);
            Assert.DoesNotContain('�', window.Text);
            assembled.Append(window.Text);
            offset = window.NextOffset;
        }

        Assert.Equal(original, assembled.ToString());
    }

    [Fact]
    public async Task VerifyAndReadSealedAsync_decodes_a_genuinely_truncated_trailing_sequence_at_the_true_end_leniently()
    {
        // A raw byte sequence whose final byte is only the lead byte of a 4-byte codepoint —
        // never completed, because this is the file's true, final end (unlike a live partial
        // file, nothing more is ever coming). This must still terminate (never hold back
        // forever waiting for bytes that will never arrive) and is display-safe via the
        // standard replacement character.
        var (runId, attemptId) = NewIds();
        var partialPath = _store.GetPartialPath(runId, attemptId, ArtifactPurpose.ProcessStandardOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        var malformedBytes = new byte[] { (byte)'o', (byte)'k', 0xF0 };
        await File.WriteAllBytesAsync(partialPath, malformedBytes);

        var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.ProcessStandardOutput, CancellationToken.None);
        var window = await _store.VerifyAndReadSealedAsync(
            sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, 0, 1024, CancellationToken.None);

        Assert.Equal(SealedReadStatus.Ok, window.Status);
        Assert.Equal(3, window.NextOffset);
        Assert.StartsWith("ok", window.Text);
        Assert.Contains('�', window.Text);
    }

    [Fact]
    public void DeleteOrphanedPartialFile_is_a_safe_no_op_when_nothing_exists()
    {
        var (runId, attemptId) = NewIds();

        _store.DeleteOrphanedPartialFile(runId, attemptId, ArtifactPurpose.ProcessStandardOutput);
    }

    private static string ExpectedHash(string content) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
