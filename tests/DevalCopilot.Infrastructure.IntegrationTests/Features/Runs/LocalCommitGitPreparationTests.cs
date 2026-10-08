using System.Text;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>ADR-0029 preparation against real Git: exact immutable tree construction, refusals and converter suppression.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class LocalCommitGitPreparationTests : IDisposable
{
    private readonly LocalCommitScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    public async Task Preparation_builds_the_exact_tree_without_touching_branch_index_or_files()
    {
        var binary = new byte[] { 0, 1, 2, 255, 0, 10, 13, 0 };
        _scene.WriteWorkspace("a.txt", "a2\n");
        _scene.WriteWorkspace("run.sh", "#!/bin/sh\necho changed\n");
        _scene.WriteWorkspace("new/readme.md", "new file\n");
        _scene.WriteWorkspaceBytes("blob.bin", binary);
        File.Delete(Path.Combine(_scene.WorkspacePath, "d.txt"));
        var indexBefore = File.ReadAllBytes(_scene.IndexPath);
        var mainBefore = _scene.MainRepositoryFingerprint();

        var (result, request) = await _scene.PrepareAsync();

        Assert.Equal(LocalCommitPreparationOutcome.Prepared, result.Outcome);
        var facts = result.Facts!;
        Assert.Equal(5, facts.ChangedPathCount);
        var entries = _scene.RunMainGit("ls-tree", "-r", facts.CommitSha).Replace("\r", string.Empty);
        Assert.Contains("100755 blob", entries.Split('\n').Single(line => line.EndsWith("\trun.sh", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("100644 blob", entries.Split('\n').Single(line => line.EndsWith("\tblob.bin", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.DoesNotContain("d.txt", entries, StringComparison.Ordinal);
        Assert.Equal("a2\n", _scene.RunMainGit("cat-file", "-p", facts.CommitSha + ":a.txt").Replace("\r", string.Empty));
        Assert.Equal(_scene.BaselineCommit, _scene.RunMainGit("rev-parse", facts.CommitSha + "^").Trim());
        Assert.Equal(facts.TreeSha, _scene.RunMainGit("rev-parse", facts.CommitSha + "^{tree}").Trim());

        // Preparation creates objects and an owned artifact only: no ref, real index, working file or main checkout moved.
        Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
        Assert.Equal(indexBefore, File.ReadAllBytes(_scene.IndexPath));
        Assert.Equal("a2\n", File.ReadAllText(Path.Combine(_scene.WorkspacePath, "a.txt")));
        Assert.Equal(mainBefore, _scene.MainRepositoryFingerprint());
        Assert.True(File.Exists(_scene.Storage.ResolveArtifact(facts.PreparedIndexRelativePath)));

        var binaryBlob = _scene.RunMainGit("rev-parse", facts.CommitSha + ":blob.bin").Trim();
        Assert.Equal(
            Convert.ToHexString(binary),
            Convert.ToHexString(ReadBlob(binaryBlob)));
        Assert.Equal(request.OperationId, Guid.Parse(
            _scene.RunMainGit("cat-file", "-p", facts.CommitSha).Split("DevalCopilot-Operation: ")[1].Trim()));
    }

    [Fact]
    public async Task Private_observation_matches_the_raw_checkpoint_for_executable_unicode_add_delete_and_binary_changes()
    {
        // The unchanged 100755 run.sh and this modified Unicode tracked name expose the two Windows compatibility settings.
        // The other changes guard that the exact raw status/diff/fingerprint algorithm remains complete.
        _scene.WriteWorkspace("naïve-漢.txt", "changed unicode\n");
        _scene.WriteWorkspace("added.txt", "new\n");
        _scene.WriteWorkspaceBytes("binary.bin", [0, 1, 0, 255, 42]);
        File.Delete(Path.Combine(_scene.WorkspacePath, "d.txt"));

        var (result, _) = await _scene.PrepareAsync();

        Assert.Equal(LocalCommitPreparationOutcome.Prepared, result.Outcome);
        Assert.Equal(4, result.Facts!.ChangedPathCount);
        var entries = _scene.RunMainGit("ls-tree", "-r", result.Facts.CommitSha).Replace("\r", string.Empty);
        Assert.Equal("changed unicode\n", _scene.RunMainGit("cat-file", "-p", result.Facts.CommitSha + ":naïve-漢.txt").Replace("\r", string.Empty));
        Assert.Contains("added.txt", entries, StringComparison.Ordinal);
        Assert.Contains("binary.bin", entries, StringComparison.Ordinal);
        Assert.DoesNotContain("\td.txt", entries, StringComparison.Ordinal);
        Assert.Contains("100755 blob", entries.Split('\n').Single(line => line.EndsWith("\trun.sh", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void Private_observation_profile_contains_both_required_checkpoint_compatibility_settings()
    {
        var profile = LocalCommitGit.BuildPrivateObservationProfile(@"C:\\controlled git", @"C:\\work tree");

        Assert.Contains("core.filemode=false", profile);
        Assert.Contains("core.quotePath=true", profile);
        Assert.Equal("core.quotePath=true", profile[^1]);
    }

    [Fact]
    public async Task Lf_bytes_pass_the_conversion_identity_control_under_a_text_attribute()
    {
        _scene.WriteWorkspace(".gitattributes", "* text=auto eol=lf\n");
        _scene.WriteWorkspace("a.txt", "lf only\n");

        var (result, _) = await _scene.PrepareAsync();

        Assert.Equal(LocalCommitPreparationOutcome.Prepared, result.Outcome);
    }

    [Fact]
    public async Task Crlf_bytes_that_the_attribute_would_normalize_are_refused_never_committed()
    {
        _scene.WriteWorkspace(".gitattributes", "* text=auto eol=lf\n");
        _scene.WriteWorkspaceBytes("a.txt", Encoding.ASCII.GetBytes("crlf\r\nline\r\n"));

        var (result, _) = await _scene.PrepareAsync();

        Assert.Equal(LocalCommitPreparationOutcome.ConversionRefused, result.Outcome);
        Assert.Equal(_scene.BaselineCommit, _scene.RunWorkspaceGit("rev-parse", "refs/heads/" + _scene.BranchName).Trim());
    }

    [Fact]
    public async Task A_clean_filter_attribute_refuses_before_the_filter_can_ever_run()
    {
        var sentinel = Path.Combine(_scene.Root, "filter-ran.txt");
        _scene.RunMainGit("config", "filter.sentinel.clean", $"cmd /c echo ran > \"{sentinel}\"");
        _scene.WriteWorkspace(".gitattributes", "a.txt filter=sentinel\n");
        _scene.WriteWorkspace("a.txt", "filtered\n");
        var (fingerprint, changes) = await _scene.CaptureAsync();
        File.Delete(sentinel);

        var (result, _) = await _scene.PrepareAsync(fingerprintOverride: fingerprint, changesOverride: changes);

        Assert.Equal(LocalCommitPreparationOutcome.ConversionRefused, result.Outcome);
        Assert.False(File.Exists(sentinel), "preparation must refuse before any configured filter can run");
    }

    [Fact]
    public async Task Ident_and_working_tree_encoding_attributes_refuse()
    {
        _scene.WriteWorkspace(".gitattributes", "a.txt ident\nsub/q.txt working-tree-encoding=UTF-16\n");
        _scene.WriteWorkspace("a.txt", "x\n");

        var (result, _) = await _scene.PrepareAsync();

        Assert.Equal(LocalCommitPreparationOutcome.ConversionRefused, result.Outcome);
    }

    [Fact]
    public async Task Repository_info_attributes_is_an_unrepresented_source_and_refuses()
    {
        Directory.CreateDirectory(Path.Combine(_scene.CommonDirectory, "info"));
        File.WriteAllText(Path.Combine(_scene.CommonDirectory, "info", "attributes"), "a.txt text\n");
        _scene.WriteWorkspace("a.txt", "x\n");

        var (result, _) = await _scene.PrepareAsync();

        Assert.Equal(LocalCommitPreparationOutcome.AttributeSourceUnrepresented, result.Outcome);
    }

    [Fact]
    public async Task An_ignored_attribute_file_on_a_changed_path_is_unrepresented_and_refuses()
    {
        _scene.WriteWorkspace(".gitignore", "sub/.gitattributes\n");
        _scene.WriteWorkspace("sub/.gitattributes", "*.txt text\n");
        _scene.WriteWorkspace("sub/q.txt", "changed\n");

        var (result, _) = await _scene.PrepareAsync();

        Assert.Equal(LocalCommitPreparationOutcome.AttributeSourceUnrepresented, result.Outcome);
    }

    [Fact]
    public async Task A_hard_linked_source_is_refused_before_its_content_reaches_git()
    {
        var outside = Path.Combine(_scene.Root, "outside.txt");
        File.WriteAllText(outside, "linked content\n");
        HardLinkSupport.Create(Path.Combine(_scene.WorkspacePath, "linked.txt"), outside);

        var (result, _) = await _scene.PrepareAsync();

        Assert.Equal(LocalCommitPreparationOutcome.UnsafeSource, result.Outcome);
        Assert.False(_scene.RunMainGit("count-objects", "-v").Contains("garbage: 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_staged_change_means_the_real_index_does_not_match_the_parent_and_refuses()
    {
        _scene.WriteWorkspace("a.txt", "staged\n");
        _scene.RunWorkspaceGit("add", "a.txt");

        var (result, _) = await _scene.PrepareAsync();

        Assert.NotEqual(LocalCommitPreparationOutcome.Prepared, result.Outcome);
    }

    [Fact]
    public async Task An_edit_after_the_checkpoint_fingerprint_refuses_as_not_current()
    {
        _scene.WriteWorkspace("a.txt", "approved\n");
        var (fingerprint, changes) = await _scene.CaptureAsync();
        _scene.WriteWorkspace("a.txt", "edited later\n");

        var (result, _) = await _scene.PrepareAsync(fingerprintOverride: fingerprint, changesOverride: changes);

        Assert.Equal(LocalCommitPreparationOutcome.CheckpointNotCurrent, result.Outcome);
    }

    [Fact]
    public async Task More_than_128_changed_paths_refuse_and_an_empty_change_refuses()
    {
        var empty = await _scene.PrepareAsync(changesOverride: []);
        Assert.Equal(LocalCommitPreparationOutcome.UnsupportedChange, empty.Result.Outcome);

        var many = Enumerable.Range(0, 129)
            .Select(index => new LocalCommitChangedPath($"many/f{index}.txt", "?", "?"))
            .ToArray();
        var (result, _) = await _scene.PrepareAsync(fingerprintOverride: new string('a', 64), changesOverride: many);
        Assert.Equal(LocalCommitPreparationOutcome.TooManyPaths, result.Outcome);
    }

    [Fact]
    public async Task A_file_over_256_KiB_and_a_total_over_4_MiB_refuse()
    {
        _scene.WriteWorkspaceBytes("big.bin", new byte[256 * 1024 + 1]);
        var (large, _) = await _scene.PrepareAsync();
        Assert.Equal(LocalCommitPreparationOutcome.SourceTooLarge, large.Outcome);

        File.Delete(Path.Combine(_scene.WorkspacePath, "big.bin"));
        for (var index = 0; index < 17; index++)
        {
            _scene.WriteWorkspaceBytes($"chunk{index}.bin", Enumerable.Repeat((byte)(index + 1), 255 * 1024).ToArray());
        }

        var (total, _) = await _scene.PrepareAsync();
        Assert.Equal(LocalCommitPreparationOutcome.TotalTooLarge, total.Outcome);
    }

    [Fact]
    public async Task An_unsupported_path_spelling_and_a_non_plain_status_refuse()
    {
        var spelling = await _scene.PrepareAsync(changesOverride: [new LocalCommitChangedPath(".git/config", "?", "?")]);
        Assert.Equal(LocalCommitPreparationOutcome.UnsupportedPath, spelling.Result.Outcome);

        var rename = await _scene.PrepareAsync(changesOverride: [new LocalCommitChangedPath("a.txt", "R", " ")]);
        Assert.Equal(LocalCommitPreparationOutcome.UnsupportedChange, rename.Result.Outcome);
    }

    [Fact]
    public async Task A_proposed_commit_on_a_moved_branch_or_foreign_marker_refuses()
    {
        _scene.WriteWorkspace("a.txt", "moved\n");
        var (fingerprint, changes) = await _scene.CaptureAsync();
        var foreign = _scene.Ownership with { LeaseId = Guid.NewGuid() };
        var request = new LocalCommitPreparationRequest(
            Guid.NewGuid(), _scene.MainPath, _scene.WorkspacePath, _scene.BranchName, _scene.BaselineCommit, fingerprint, changes,
            LocalCommitScene.Message, foreign, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));

        var result = await _scene.Git.PrepareAsync(request, CancellationToken.None);

        Assert.Equal(LocalCommitPreparationOutcome.OwnershipNotProven, result.Outcome);
    }

    [Fact]
    public async Task A_non_empty_hooks_directory_refuses_every_git_command()
    {
        Assert.True(_scene.Storage.TryEnsureHooksDirectoryEmpty());
        File.WriteAllText(Path.Combine(_scene.Storage.HooksDirectory, "reference-transaction"), "#!/bin/sh\nexit 0\n");
        _scene.WriteWorkspace("a.txt", "x\n");

        var (result, _) = await _scene.PrepareAsync();

        Assert.Equal(LocalCommitPreparationOutcome.HooksDirectoryNotEmpty, result.Outcome);
    }

    private byte[] ReadBlob(string blobId)
    {
        var path = Path.Combine(_scene.Root, "blob.out");
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("git", $"-C \"{_scene.MainPath}\" cat-file blob {blobId}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            },
        };
        process.Start();
        using var memory = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(memory);
        process.WaitForExit();
        File.WriteAllBytes(path, memory.ToArray());
        return memory.ToArray();
    }
}
