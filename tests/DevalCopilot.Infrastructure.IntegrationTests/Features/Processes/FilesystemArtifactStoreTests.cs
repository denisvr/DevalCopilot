using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
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
    public async Task Verification_output_is_sealed_under_the_application_artifact_root_and_can_be_verified()
    {
        var executionId = Guid.NewGuid();
        var partialPath = _store.GetPartialPath(executionId, VerificationOutputPurpose.StandardOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllTextAsync(partialPath, "redacted output");

        var sealedFile = await _store.SealAsync(executionId, VerificationOutputPurpose.StandardOutput, CancellationToken.None);

        Assert.NotNull(sealedFile);
        Assert.StartsWith("verifications", sealedFile.RelativeStoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetFullPath(_root), Path.GetFullPath(Path.Combine(_root, sealedFile.RelativeStoragePath)), StringComparison.OrdinalIgnoreCase);
        var read = await _store.VerifyAndReadSealedAsync(
            sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, 0, 1024, CancellationToken.None);

        Assert.Equal(SealedReadStatus.Ok, read.Status);
        Assert.Equal("redacted output", read.Text);
    }

    [Fact]
    public async Task Verification_output_integrity_mismatch_returns_no_text()
    {
        var executionId = Guid.NewGuid();
        var partialPath = _store.GetPartialPath(executionId, VerificationOutputPurpose.StandardError);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllTextAsync(partialPath, "original");
        var sealedFile = await _store.SealAsync(executionId, VerificationOutputPurpose.StandardError, CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(_root, sealedFile!.RelativeStoragePath), "tampered");
        var read = await _store.VerifyAndReadSealedAsync(
            sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, 0, 1024, CancellationToken.None);

        Assert.Equal(SealedReadStatus.IntegrityMismatch, read.Status);
        Assert.Empty(read.Text);
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

    // Physical-containment hardening: lexical containment (Path.GetFullPath, already exercised
    // above) proves nothing once a reparse point sits on the chain between the artifact root and
    // the sealed file — the OS can physically open a completely different location than the
    // string implies. These tests use the same non-elevated fixture techniques already
    // established and reviewed in PackageEntrypointResolverTests (a directory junction via plain
    // `mklink /J`, which needs no elevation on modern Windows, and a file-level symbolic link,
    // gracefully skipped when this host lacks the privilege/Developer Mode that requires).

    [Fact]
    public async Task VerifyAndReadSealedAsync_still_serves_bytes_for_an_ordinary_agent_context_manifest_artifact()
    {
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, "the agent's context manifest");
        var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);

        var read = await _store.VerifyAndReadSealedAsync(
            sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, 0, 1024, CancellationToken.None);

        Assert.Equal(SealedReadStatus.Ok, read.Status);
        Assert.Equal("the agent's context manifest", read.Text);
    }

    [Fact]
    public async Task VerifyAndReadSealedAsync_reads_a_multi_window_file_whose_full_content_matches_the_hash_verified_from_the_same_opened_handle()
    {
        // A distinctive, non-repeating pattern long enough to require several small windows —
        // if the implementation ever hashed one opened file but windowed-read a DIFFERENT one
        // (e.g. a reopen-by-path race), the reconstructed text below would not byte-for-byte
        // match the exact content whose hash was independently computed here and supplied as
        // the expected hash, proving both operations read from the one identity this method
        // opens exactly once.
        var original = string.Concat(Enumerable.Range(0, 100).Select(i => $"[{i:D4}]"));
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.AgentStandardOutput, original);
        var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.AgentStandardOutput, CancellationToken.None);
        Assert.Equal(ExpectedHash(original), sealedFile!.ContentHash);

        var assembled = new StringBuilder();
        long offset = 0;
        for (var iterations = 0; iterations < 100 && offset < sealedFile.ByteLength; iterations++)
        {
            var window = await _store.VerifyAndReadSealedAsync(
                sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, offset, 17, CancellationToken.None);
            Assert.Equal(SealedReadStatus.Ok, window.Status);
            assembled.Append(window.Text);
            offset = window.NextOffset;
        }

        Assert.Equal(original, assembled.ToString());
    }

    [WindowsOnlyFact]
    public async Task VerifyAndReadSealedAsync_returns_missing_when_an_intermediate_directory_is_a_reparse_point_redirecting_outside_the_root_even_when_length_and_hash_match()
    {
        const string sentinelContent = "sentinel content living entirely outside the artifact root";
        var outsideDirectory = Path.Combine(Path.GetTempPath(), $"devalcopilot-artifact-store-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDirectory);
        var (runId, attemptId) = NewIds();

        try
        {
            // The sentinel file is named and shaped exactly like a real sealed artifact would be,
            // but lives entirely outside the root. Its own real length/hash are supplied as the
            // "expected" values below — a hash/length-only check would consider this a perfect
            // match; only physical containment can reject it.
            var sentinelFile = Path.Combine(outsideDirectory, "final-response.sealed");
            await File.WriteAllTextAsync(sentinelFile, sentinelContent);

            var attemptDirectory = Path.Combine(_root, "runs", runId.ToString(), "attempts");
            Directory.CreateDirectory(attemptDirectory);
            var junctionPath = Path.Combine(attemptDirectory, attemptId.ToString());

            var junctionCreation = TryCreateJunction(junctionPath, outsideDirectory);
            try
            {
                Assert.True(
                    junctionCreation.Succeeded,
                    $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

                var relativeStoragePath = _store.GetSealedRelativePath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
                var read = await _store.VerifyAndReadSealedAsync(
                    relativeStoragePath, sentinelContent.Length, ExpectedHash(sentinelContent), 0, 1024, CancellationToken.None);

                Assert.Equal(SealedReadStatus.Missing, read.Status);
                Assert.Equal(string.Empty, read.Text);
            }
            finally
            {
                if (Directory.Exists(junctionPath))
                {
                    Directory.Delete(junctionPath);
                }
            }
        }
        finally
        {
            if (Directory.Exists(outsideDirectory))
            {
                Directory.Delete(outsideDirectory, recursive: true);
            }
        }
    }

    [RequiresFileSymlinkSupportFact]
    public async Task VerifyAndReadSealedAsync_returns_missing_when_the_sealed_file_itself_is_a_symlink_to_an_outside_sentinel_even_when_length_and_hash_match()
    {
        const string sentinelContent = "sentinel content reached only through a file-level symlink";
        var outsideDirectory = Path.Combine(Path.GetTempPath(), $"devalcopilot-artifact-store-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDirectory);
        var (runId, attemptId) = NewIds();

        try
        {
            var sentinelFile = Path.Combine(outsideDirectory, "sentinel.txt");
            await File.WriteAllTextAsync(sentinelFile, sentinelContent);

            var relativeStoragePath = _store.GetSealedRelativePath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
            var linkPath = Path.Combine(_root, relativeStoragePath);
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

            // The attribute above already proved this host can create a file symlink; a failure
            // here would be a genuine, newly-introduced regression, not an expected
            // environmental gap.
            File.CreateSymbolicLink(linkPath, sentinelFile);

            var read = await _store.VerifyAndReadSealedAsync(
                relativeStoragePath, sentinelContent.Length, ExpectedHash(sentinelContent), 0, 1024, CancellationToken.None);

            Assert.Equal(SealedReadStatus.Missing, read.Status);
            Assert.Equal(string.Empty, read.Text);
        }
        finally
        {
            if (Directory.Exists(outsideDirectory))
            {
                Directory.Delete(outsideDirectory, recursive: true);
            }
        }
    }

    [WindowsOnlyFact]
    public async Task VerifyAndReadSealedAsync_still_serves_bytes_when_the_configured_artifact_root_itself_is_a_reparse_point()
    {
        // A root that is ITSELF a legitimate junction (for example a redirected profile
        // directory) is not an attack: both the root and the sealed file resolve, through the
        // SAME junction, to the SAME real underlying location, so containment correctly holds.
        // Physical containment is resolved fresh against whatever the configured root currently
        // really refers to — never a cached assumption about the root's own configured string.
        var realRootTarget = Path.Combine(Path.GetTempPath(), $"devalcopilot-artifact-store-real-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(realRootTarget);
        var junctionRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-artifact-store-junction-root-{Guid.NewGuid():N}");

        try
        {
            var junctionCreation = TryCreateJunction(junctionRoot, realRootTarget);
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            var storeOverJunctionedRoot = new FilesystemArtifactStore(junctionRoot);
            var (runId, attemptId) = NewIds();
            var partialPath = storeOverJunctionedRoot.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
            await File.WriteAllTextAsync(partialPath, "real content behind the junctioned root");
            var sealedFile = await storeOverJunctionedRoot.SealAsync(runId, attemptId, ArtifactPurpose.AgentFinalResponse, CancellationToken.None);

            var read = await storeOverJunctionedRoot.VerifyAndReadSealedAsync(
                sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, 0, 1024, CancellationToken.None);

            Assert.Equal(SealedReadStatus.Ok, read.Status);
            Assert.Equal("real content behind the junctioned root", read.Text);
        }
        finally
        {
            if (Directory.Exists(junctionRoot))
            {
                Directory.Delete(junctionRoot);
            }

            if (Directory.Exists(realRootTarget))
            {
                Directory.Delete(realRootTarget, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VerifyAndReadSealedAsync_returns_missing_for_a_dot_dot_route_into_a_case_distinct_sibling_spelling_of_the_root_even_when_the_file_is_reachable_and_verifies()
    {
        // On a case-insensitive host the upper-cased spelling of the root's own directory name
        // resolves to the very same real directory, so the file below is genuinely reachable and
        // its length/hash genuinely match: only the containment comparison can reject it. A
        // case-insensitive prefix comparison would have accepted it and returned Ok.
        var (runId, attemptId) = NewIds();
        await WritePartialAsync(runId, attemptId, ArtifactPurpose.AgentFinalResponse, "reachable through a case-distinct spelling");
        var sealedFile = await _store.SealAsync(runId, attemptId, ArtifactPurpose.AgentFinalResponse, CancellationToken.None);
        var caseDistinctRootName = Path.GetFileName(_root).ToUpperInvariant();
        Assert.NotEqual(Path.GetFileName(_root), caseDistinctRootName);

        var viaCaseDistinctSibling = Path.Combine("..", caseDistinctRootName, sealedFile!.RelativeStoragePath);
        var read = await _store.VerifyAndReadSealedAsync(
            viaCaseDistinctSibling, sealedFile.ByteLength, sealedFile.ContentHash, 0, 1024, CancellationToken.None);

        Assert.Equal(SealedReadStatus.Missing, read.Status);
        Assert.Equal(string.Empty, read.Text);

        var direct = await _store.VerifyAndReadSealedAsync(
            sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, 0, 1024, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, direct.Status);
    }

    [RequiresCaseSensitiveDirectorySupportFact]
    public async Task VerifyAndReadSealedAsync_returns_missing_when_a_junction_inside_the_root_resolves_to_a_case_distinct_sibling_of_the_root()
    {
        // The lexical path stays inside the root (only a junction segment differs), so only the
        // opened-handle comparison can reject: the file's real path sits under a directory that
        // differs from the root's real path by letter case alone.
        const string content = "content living in a case-distinct sibling directory";
        var parent = Path.Combine(Path.GetTempPath(), $"devalcopilot-case-sibling-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);

        try
        {
            Assert.True(RequiresCaseSensitiveDirectorySupportFactAttribute.TryEnableCaseSensitivity(parent));
            var realRoot = Path.Combine(parent, "Artifacts");
            var sibling = Path.Combine(parent, "ARTIFACTS");
            Directory.CreateDirectory(realRoot);
            Directory.CreateDirectory(sibling);
            await File.WriteAllTextAsync(Path.Combine(sibling, "final-response.sealed"), content);

            var store = new FilesystemArtifactStore(realRoot);
            var junction = Path.Combine(realRoot, "linked");
            var junctionCreation = TryCreateJunction(junction, sibling);
            try
            {
                Assert.True(
                    junctionCreation.Succeeded,
                    $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

                var read = await store.VerifyAndReadSealedAsync(
                    Path.Combine("linked", "final-response.sealed"), content.Length, ExpectedHash(content), 0, 1024, CancellationToken.None);

                Assert.Equal(SealedReadStatus.Missing, read.Status);
                Assert.Equal(string.Empty, read.Text);
            }
            finally
            {
                if (Directory.Exists(junction))
                {
                    Directory.Delete(junction);
                }
            }
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    /// <summary>Never leaks either the junction or target path: only the process's own exit code
    /// and stderr text are surfaced to a failing assertion.</summary>
    private readonly record struct JunctionCreationResult(bool Succeeded, int ExitCode, string StandardError);

    private static JunctionCreationResult TryCreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(junctionPath);
            startInfo.ArgumentList.Add(targetPath);

            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            var succeeded = process.ExitCode == 0 && Directory.Exists(junctionPath);
            return new JunctionCreationResult(succeeded, process.ExitCode, standardError);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new JunctionCreationResult(false, -1, exception.GetType().Name);
        }
    }
}
