using System.Diagnostics;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0029 R7 at the Infrastructure boundary: the bounded, read-only proof about the owned reference lock and the HEAD lock. Only a
/// positively observed absence through a redirection-free path is Clear; a lock of any kind, bytes or age is Present and untouched;
/// everything that cannot be represented or read is Unproven. The probe never creates, reads, renames or deletes anything.
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class LocalCommitReferenceLocksTests : IDisposable
{
    private const string Branch = "devalcopilot/workspace/test/1";
    private const string Sentinel = "FOREIGN-LOCK-SENTINEL\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-reference-locks-{Guid.NewGuid():N}");
    private readonly List<LocalCommitScene> _scenes = [];

    public LocalCommitReferenceLocksTests()
    {
        Directory.CreateDirectory(CommonDirectory);
        Directory.CreateDirectory(AdministrativeDirectory);
    }

    public void Dispose()
    {
        foreach (var scene in _scenes)
        {
            scene.Dispose();
        }

        if (Directory.Exists(_root))
        {
            RemoveJunctions(_root);
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A recursive delete cannot cross a junction on Windows, so each one is removed as a link first, never through its target.</summary>
    private static void RemoveJunctions(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(child, recursive: false);
            }
            else
            {
                RemoveJunctions(child);
            }
        }
    }

    private string CommonDirectory => Path.Combine(_root, "common");

    private string AdministrativeDirectory => Path.Combine(_root, "common", "worktrees", "one");

    private string RefLock => Path.Combine(CommonDirectory, "refs", "heads", "devalcopilot", "workspace", "test", "1.lock");

    private string HeadLock => Path.Combine(AdministrativeDirectory, "HEAD.lock");

    private LocalCommitReferenceLockState Probe(string? branch = null) =>
        LocalCommitReferenceLocks.Probe(CommonDirectory, AdministrativeDirectory, branch ?? Branch);

    private void WriteRefLock(string content = Sentinel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RefLock)!);
        File.WriteAllText(RefLock, content);
    }

    private void CreateRefsHeads() => Directory.CreateDirectory(Path.Combine(CommonDirectory, "refs", "heads", "devalcopilot", "workspace", "test"));

    [Fact]
    public void An_existing_namespace_without_either_lock_is_clear()
    {
        CreateRefsHeads();

        Assert.Equal(LocalCommitReferenceLockState.Clear, Probe());
    }

    [Fact]
    public void A_definitely_absent_ancestor_proves_the_lock_cannot_exist()
    {
        // Neither refs/heads nor any branch directory exists yet, which is positive evidence that no reference lock exists.
        Assert.Equal(LocalCommitReferenceLockState.Clear, Probe());
        Directory.CreateDirectory(Path.Combine(CommonDirectory, "refs", "heads"));
        Assert.Equal(LocalCommitReferenceLockState.Clear, Probe());
    }

    [Fact]
    public void The_owned_reference_lock_is_present()
    {
        WriteRefLock();

        Assert.Equal(LocalCommitReferenceLockState.Present, Probe());
    }

    [Fact]
    public void The_head_lock_is_present()
    {
        File.WriteAllText(HeadLock, Sentinel);

        Assert.Equal(LocalCommitReferenceLockState.Present, Probe());
    }

    [Fact]
    public void Either_lock_present_wins_even_when_the_other_namespace_is_unproven()
    {
        WriteRefLock();

        Assert.Equal(
            LocalCommitReferenceLockState.Present,
            LocalCommitReferenceLocks.Probe(CommonDirectory, Path.Combine(_root, "missing-administrative-directory"), Branch));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(100_000, 400)]
    public void A_lock_is_present_whatever_its_bytes_or_age_so_nothing_is_ever_taken_as_ownership(int length, int ageDays)
    {
        WriteRefLock(new string('x', length));
        File.SetLastWriteTimeUtc(RefLock, DateTime.UtcNow.AddDays(-ageDays));
        File.SetCreationTimeUtc(RefLock, DateTime.UtcNow.AddDays(-ageDays));

        Assert.Equal(LocalCommitReferenceLockState.Present, Probe());
    }

    [Fact]
    public void A_directory_that_occupies_the_lock_name_is_present_because_it_blocks_the_lock()
    {
        Directory.CreateDirectory(RefLock);

        Assert.Equal(LocalCommitReferenceLockState.Present, Probe());
    }

    [Fact]
    public void A_read_only_hidden_lock_is_still_present()
    {
        WriteRefLock();
        File.SetAttributes(RefLock, FileAttributes.ReadOnly | FileAttributes.Hidden);

        Assert.Equal(LocalCommitReferenceLockState.Present, Probe());

        File.SetAttributes(RefLock, FileAttributes.Normal);
    }

    [Fact]
    public void The_probe_never_changes_the_filesystem()
    {
        WriteRefLock();
        File.WriteAllText(HeadLock, "head lock bytes");
        var before = Snapshot();

        Probe();
        Probe("a/b");
        LocalCommitReferenceLocks.Probe(Path.Combine(_root, "absent"), AdministrativeDirectory, Branch);

        Assert.Equal(before, Snapshot());
        Assert.Equal(Sentinel, File.ReadAllText(RefLock));
        Assert.Equal("head lock bytes", File.ReadAllText(HeadLock));
    }

    private string[] Snapshot() => Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => path + "|" + (File.Exists(path) ? File.ReadAllText(path) + "|" + File.GetLastWriteTimeUtc(path).Ticks : "dir"))
        .ToArray();

    public enum UnreadableAt
    {
        CommonRoot,
        AdministrativeRoot,
        RefsDirectory,
        HeadsDirectory,
        BranchDirectory,
        ReferenceLock,
        HeadLock,
    }

    public static TheoryData<UnreadableAt, string> UnreadableCases()
    {
        var data = new TheoryData<UnreadableAt, string>();
        foreach (var at in Enum.GetValues<UnreadableAt>())
        {
            foreach (var fault in new[] { nameof(UnauthorizedAccessException), nameof(IOException), nameof(PathTooLongException), nameof(NotSupportedException) })
            {
                data.Add(at, fault);
            }
        }

        return data;
    }

    private string PathOf(UnreadableAt at) => at switch
    {
        UnreadableAt.CommonRoot => CommonDirectory,
        UnreadableAt.AdministrativeRoot => AdministrativeDirectory,
        UnreadableAt.RefsDirectory => Path.Combine(CommonDirectory, "refs"),
        UnreadableAt.HeadsDirectory => Path.Combine(CommonDirectory, "refs", "heads"),
        UnreadableAt.BranchDirectory => Path.Combine(CommonDirectory, "refs", "heads", "devalcopilot"),
        UnreadableAt.ReferenceLock => RefLock,
        _ => HeadLock,
    };

    private static Exception FaultNamed(string name) => name switch
    {
        nameof(UnauthorizedAccessException) => new UnauthorizedAccessException("denied"),
        nameof(IOException) => new IOException("broken"),
        nameof(PathTooLongException) => new PathTooLongException(),
        _ => new NotSupportedException("unsupported"),
    };

    [Theory]
    [MemberData(nameof(UnreadableCases))]
    public void A_path_that_cannot_be_read_is_unproven_never_clear_whatever_the_fault(UnreadableAt at, string fault)
    {
        CreateRefsHeads();
        var unreadable = PathOf(at);

        var state = LocalCommitReferenceLocks.Probe(
            CommonDirectory, AdministrativeDirectory, Branch,
            path => string.Equals(path, unreadable, StringComparison.OrdinalIgnoreCase) ? throw FaultNamed(fault) : File.GetAttributes(path));

        Assert.Equal(LocalCommitReferenceLockState.Unproven, state);
    }

    [Fact]
    public void A_definite_not_found_is_the_only_read_failure_that_proves_absence()
    {
        var state = LocalCommitReferenceLocks.Probe(
            CommonDirectory, AdministrativeDirectory, Branch,
            path => path.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ? throw new FileNotFoundException() : File.GetAttributes(path));

        Assert.Equal(LocalCommitReferenceLockState.Clear, state);
    }

    [Fact]
    public void A_present_lock_is_reported_even_when_the_other_lock_cannot_be_read()
    {
        File.WriteAllText(HeadLock, Sentinel);

        var state = LocalCommitReferenceLocks.Probe(
            CommonDirectory, AdministrativeDirectory, Branch,
            path => path.StartsWith(Path.Combine(CommonDirectory, "refs"), StringComparison.OrdinalIgnoreCase)
                ? throw new UnauthorizedAccessException()
                : File.GetAttributes(path));

        Assert.Equal(LocalCommitReferenceLockState.Present, state);
    }

    public static TheoryData<string> UnrepresentableBranches => new()
    {
        string.Empty,
        "a/../b",
        "a//b",
        "/a",
        "a/",
        ".hidden",
        "a/.hidden",
        "a.",
        "a..b",
        "a/b.lock",
        "a.LOCK",
        "a b",
        "a:b",
        "a\\b",
        "a*b",
        "a?b",
        "a[b",
        "a~b",
        "a^b",
        "x/./y",
        "caf" + char.ConvertFromUtf32(0xE9),
        "a@{b",
        "nul\0name",
        "refs/heads/../../x",
    };

    [Theory]
    [MemberData(nameof(UnrepresentableBranches))]
    public void A_branch_that_cannot_be_mapped_to_a_fixed_lock_path_is_unproven_not_clear(string branch)
    {
        CreateRefsHeads();

        Assert.Equal(LocalCommitReferenceLockState.Unproven, Probe(branch));
    }

    [Fact]
    public void An_overlong_or_overdeep_branch_is_unproven()
    {
        Assert.Equal(LocalCommitReferenceLockState.Unproven, Probe(new string('a', 257)));
        Assert.Equal(LocalCommitReferenceLockState.Unproven, Probe(string.Join('/', Enumerable.Repeat("a", 17))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\common")]
    public void A_directory_that_is_not_fully_qualified_is_unproven(string common)
    {
        Assert.Equal(LocalCommitReferenceLockState.Unproven, LocalCommitReferenceLocks.Probe(common, AdministrativeDirectory, Branch));
        Assert.Equal(LocalCommitReferenceLockState.Unproven, LocalCommitReferenceLocks.Probe(CommonDirectory, common, Branch));
    }

    [Fact]
    public void A_missing_root_directory_is_unproven_not_clear()
    {
        Assert.Equal(
            LocalCommitReferenceLockState.Unproven,
            LocalCommitReferenceLocks.Probe(Path.Combine(_root, "absent-common"), AdministrativeDirectory, Branch));
        Assert.Equal(
            LocalCommitReferenceLockState.Unproven,
            LocalCommitReferenceLocks.Probe(CommonDirectory, Path.Combine(_root, "absent-administrative"), Branch));
    }

    [Fact]
    public void A_root_that_is_a_file_is_unproven()
    {
        var file = Path.Combine(_root, "a-file");
        File.WriteAllText(file, "x");

        Assert.Equal(LocalCommitReferenceLockState.Unproven, LocalCommitReferenceLocks.Probe(file, AdministrativeDirectory, Branch));
        Assert.Equal(LocalCommitReferenceLockState.Unproven, LocalCommitReferenceLocks.Probe(CommonDirectory, file, Branch));
    }

    [Fact]
    public void An_ancestor_that_is_a_file_is_unproven()
    {
        Directory.CreateDirectory(Path.Combine(CommonDirectory, "refs"));
        File.WriteAllText(Path.Combine(CommonDirectory, "refs", "heads"), "not a directory");

        Assert.Equal(LocalCommitReferenceLockState.Unproven, Probe());
    }

    [Theory]
    [InlineData("refs")]
    [InlineData("refs/heads")]
    [InlineData("refs/heads/devalcopilot")]
    [InlineData("refs/heads/devalcopilot/workspace/test")]
    public void A_redirected_ancestor_makes_the_namespace_unproven(string redirected)
    {
        CreateRefsHeads();
        RedirectDirectory(Path.Combine(CommonDirectory, redirected.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(LocalCommitReferenceLockState.Unproven, Probe());
    }

    [Fact]
    public void A_redirected_root_makes_the_namespace_unproven()
    {
        var real = Path.Combine(_root, "real-administrative");
        Directory.CreateDirectory(real);
        var link = Path.Combine(_root, "linked-administrative");
        Assert.True(TryCreateJunction(link, real), "A directory junction could not be created in this environment.");

        Assert.Equal(LocalCommitReferenceLockState.Unproven, LocalCommitReferenceLocks.Probe(CommonDirectory, link, Branch));
    }

    /// <summary>Replaces an existing directory by a junction to its relocated copy, as a redirection of the lock namespace would.</summary>
    private void RedirectDirectory(string path)
    {
        var relocated = Path.Combine(_root, "relocated-" + Guid.NewGuid().ToString("N"));
        Directory.Move(path, relocated);
        Assert.True(TryCreateJunction(path, relocated), "A directory junction could not be created in this environment.");
    }

    private static bool TryCreateJunction(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
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
        using var process = Process.Start(startInfo)!;
        process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(junctionPath);
    }

    private async Task<(LocalCommitScene Scene, LocalCommitFacts Facts)> PreparedAsync()
    {
        var scene = new LocalCommitScene();
        _scenes.Add(scene);
        scene.WriteWorkspace("a.txt", "approved change\n");
        var (result, request) = await scene.PrepareAsync();
        Assert.Equal(LocalCommitPreparationOutcome.Prepared, result.Outcome);
        return (scene, scene.FactsFor(request, result.Facts!));
    }

    [Fact]
    public async Task Real_git_inspection_of_a_clean_namespace_is_the_clear_control()
    {
        var (scene, facts) = await PreparedAsync();

        var inspection = await scene.Git.InspectAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitInspectionOutcome.Observed, inspection.Outcome);
        Assert.Equal(LocalCommitReferenceLockState.Clear, inspection.ReferenceLocks);
        Assert.Equal(scene.BaselineCommit, inspection.BranchTipSha);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_git_inspection_reports_a_foreign_reference_or_head_lock_and_leaves_it_untouched(bool head)
    {
        var (scene, facts) = await PreparedAsync();
        var lockPath = head
            ? Path.Combine(scene.AdministrativeDirectory, "HEAD.lock")
            : Path.Combine(scene.CommonDirectory, "refs", "heads", "devalcopilot", "workspace", "test", "1.lock");
        File.WriteAllText(lockPath, Sentinel);
        var indexBefore = File.ReadAllBytes(scene.IndexPath);

        var inspection = await scene.Git.InspectAsync(facts, CancellationToken.None);

        Assert.Equal(LocalCommitInspectionOutcome.Observed, inspection.Outcome);
        Assert.Equal(LocalCommitReferenceLockState.Present, inspection.ReferenceLocks);
        Assert.Equal(Sentinel, File.ReadAllText(lockPath));
        Assert.Equal(indexBefore, File.ReadAllBytes(scene.IndexPath));
        Assert.Equal(scene.BaselineCommit, scene.RunMainGit("rev-parse", "refs/heads/" + scene.BranchName).Trim());

        File.Delete(lockPath);
        Assert.Equal(
            LocalCommitReferenceLockState.Clear, (await scene.Git.InspectAsync(facts, CancellationToken.None)).ReferenceLocks);
    }

    [Fact]
    public async Task Real_git_inspection_of_a_redirected_reference_namespace_is_unproven()
    {
        var (scene, facts) = await PreparedAsync();
        var branchDirectory = Path.Combine(scene.CommonDirectory, "refs", "heads", "devalcopilot");
        var relocated = Path.Combine(scene.Root, "relocated-branch-directory");
        Directory.Move(branchDirectory, relocated);
        Assert.True(TryCreateJunction(branchDirectory, relocated), "A directory junction could not be created in this environment.");
        try
        {
            var inspection = await scene.Git.InspectAsync(facts, CancellationToken.None);

            Assert.Equal(LocalCommitInspectionOutcome.Observed, inspection.Outcome);
            Assert.Equal(LocalCommitReferenceLockState.Unproven, inspection.ReferenceLocks);
            Assert.Equal(scene.BaselineCommit, inspection.BranchTipSha);
        }
        finally
        {
            // The scene deletes its tree recursively, which must never meet the junction.
            Directory.Delete(branchDirectory, recursive: false);
            Directory.Move(relocated, branchDirectory);
        }
    }
}
