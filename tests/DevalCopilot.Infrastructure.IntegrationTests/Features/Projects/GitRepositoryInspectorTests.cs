using System.Diagnostics;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// Exercises <see cref="GitRepositoryInspector"/> against real <c>git.exe</c> and real
/// temporary repositories — the same "use the real, deterministic executable" philosophy
/// already applied to <c>ChildProcessExecutionAdapterTests</c>, since git genuinely is a
/// required tool for this application (see <c>Capability.Git</c>), not a network dependency
/// that would need a fake.
///
/// <para>
/// Shares the <see cref="EnvironmentPathMutationCollection"/> collection with
/// <c>ToolDiscoveryAdapterTests</c>, the one other test class that mutates the real,
/// process-wide <c>PATH</c> environment variable — without this, xUnit's default cross-class
/// parallelism can let that mutation be in effect while this class resolves and invokes the
/// real <c>git.exe</c>, intermittently resolving a synthetic zero-byte stub instead.
/// </para>
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class GitRepositoryInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-git-inspector-{Guid.NewGuid():N}");
    private readonly GitRepositoryInspector _inspector = new(new ChildProcessExecutionAdapter());

    public GitRepositoryInspectorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        // Git marks object-database files read-only on Windows; clear that before deleting, or
        // the recursive delete below throws UnauthorizedAccessException.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private string CreateRepo(string name, bool withCommit = true)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q");
        RunGit(path, "config", "user.email", "test@example.com");
        RunGit(path, "config", "user.name", "Test");

        if (withCommit)
        {
            File.WriteAllText(Path.Combine(path, "file.txt"), "content");
            RunGit(path, "add", "file.txt");
            RunGit(path, "commit", "-q", "-m", "initial");
        }

        return path;
    }

    private Task<GitRepositoryInspectionResult> InspectAsync(string path) =>
        _inspector.InspectAsync(new RepositoryRootCandidate(path), CancellationToken.None);

    [Fact]
    public async Task InspectAsync_reports_a_clean_repository_on_a_branch()
    {
        var path = CreateRepo("clean");

        var result = await InspectAsync(path);

        Assert.Equal(GitRepositoryInspectionOutcome.Success, result.Outcome);
        Assert.Equal(RepositoryHeadState.OnBranch, result.HeadState);
        Assert.False(string.IsNullOrEmpty(result.BranchName));
        Assert.Equal(40, result.HeadCommitSha?.Length);
        Assert.False(result.IsDirty);
    }

    [Fact]
    public async Task InspectAsync_reports_dirty_when_a_tracked_file_is_modified()
    {
        var path = CreateRepo("dirty-tracked");
        File.WriteAllText(Path.Combine(path, "file.txt"), "changed");

        var result = await InspectAsync(path);

        Assert.Equal(GitRepositoryInspectionOutcome.Success, result.Outcome);
        Assert.True(result.IsDirty);
    }

    [Fact]
    public async Task InspectAsync_reports_dirty_when_only_an_untracked_file_is_present()
    {
        var path = CreateRepo("dirty-untracked");
        File.WriteAllText(Path.Combine(path, "untracked.txt"), "new");

        var result = await InspectAsync(path);

        Assert.Equal(GitRepositoryInspectionOutcome.Success, result.Outcome);
        Assert.True(result.IsDirty);
    }

    [Fact]
    public async Task InspectAsync_reports_detached_head_with_no_branch_name()
    {
        var path = CreateRepo("detached");
        RunGit(path, "checkout", "-q", "--detach", "HEAD");

        var result = await InspectAsync(path);

        Assert.Equal(GitRepositoryInspectionOutcome.Success, result.Outcome);
        Assert.Equal(RepositoryHeadState.Detached, result.HeadState);
        Assert.Null(result.BranchName);
        Assert.Equal(40, result.HeadCommitSha?.Length);
    }

    [Fact]
    public async Task InspectAsync_fails_closed_on_a_corrupt_head_that_matches_no_valid_combination()
    {
        var path = CreateRepo("corrupt-head");

        // Empirically verified: a symbolic ref with an empty branch name after the prefix makes
        // the repository's structural facts (git-dir/is-bare-repository/show-toplevel) resolve
        // normally, but makes BOTH `symbolic-ref -q --short HEAD` and `rev-parse HEAD` exit 128
        // — neither the OnBranch, Unborn, nor Detached shape. This is deliberately not "both
        // fail entirely" (which git instead reports as "not a git repository" and is already
        // covered by InspectAsync_rejects_a_plain_non_git_directory), but a corrupt HEAD inside
        // an otherwise perfectly structurally valid repository.
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/\n");

        var result = await InspectAsync(path);

        Assert.Equal(GitRepositoryInspectionOutcome.InvalidHeadState, result.Outcome);
        Assert.Null(result.HeadState);
        Assert.Null(result.BranchName);
        Assert.Null(result.HeadCommitSha);
    }

    [Fact]
    public async Task InspectAsync_reports_unborn_with_a_branch_name_and_no_commit_sha()
    {
        var path = CreateRepo("unborn", withCommit: false);

        var result = await InspectAsync(path);

        Assert.Equal(GitRepositoryInspectionOutcome.Success, result.Outcome);
        Assert.Equal(RepositoryHeadState.Unborn, result.HeadState);
        Assert.False(string.IsNullOrEmpty(result.BranchName));
        Assert.Null(result.HeadCommitSha);
    }

    [Fact]
    public async Task InspectAsync_rejects_a_bare_repository()
    {
        var path = Path.Combine(_root, "bare.git");
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q", "--bare");

        var result = await InspectAsync(path);

        Assert.Equal(GitRepositoryInspectionOutcome.BareRepositoryNotSupported, result.Outcome);
    }

    [Fact]
    public async Task InspectAsync_rejects_a_linked_worktree()
    {
        var mainPath = CreateRepo("main-for-worktree");
        var worktreePath = Path.Combine(_root, "linked-worktree");
        RunGit(mainPath, "worktree", "add", "-q", worktreePath, "HEAD");

        var result = await InspectAsync(worktreePath);

        Assert.Equal(GitRepositoryInspectionOutcome.LinkedWorktreeNotSupported, result.Outcome);
    }

    [Fact]
    public async Task InspectAsync_rejects_a_plain_non_git_directory()
    {
        var path = Path.Combine(_root, "plain");
        Directory.CreateDirectory(path);

        var result = await InspectAsync(path);

        Assert.Equal(GitRepositoryInspectionOutcome.NotAGitRepository, result.Outcome);
    }

    [Fact]
    public async Task InspectAsync_rejects_a_subdirectory_that_is_not_the_repository_top_level()
    {
        var path = CreateRepo("subdir-parent");
        var subdirectory = Path.Combine(path, "nested");
        Directory.CreateDirectory(subdirectory);

        var result = await InspectAsync(subdirectory);

        Assert.Equal(GitRepositoryInspectionOutcome.NotTopLevelRoot, result.Outcome);
    }

    [Fact]
    public async Task InspectAsync_is_never_hijacked_by_a_repository_local_core_fsmonitor_command()
    {
        var path = CreateRepo("fsmonitor-hijack-attempt");
        var markerPath = Path.Combine(_root, "fsmonitor-executed.marker");

        // A malicious repository configuring core.fsmonitor to an arbitrary command is a real,
        // verified Git behavior for an un-overridden `git status` — this proves the fixed
        // `-c core.fsmonitor=false` hardening this inspector always applies actually prevents it.
        RunGit(path, "config", "core.fsmonitor", $"echo hijacked > \"{markerPath}\"");

        var result = await InspectAsync(path);

        Assert.Equal(GitRepositoryInspectionOutcome.Success, result.Outcome);
        Assert.False(File.Exists(markerPath));
    }
}
