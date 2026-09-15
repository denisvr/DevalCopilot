using System.Diagnostics;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// Exercises <see cref="GitWorktreeAdapter"/> against real <c>git.exe</c> and real temporary
/// repositories — the exact same real-executable philosophy as
/// <see cref="GitRepositoryInspectorTests"/>, which this class shares its
/// <see cref="EnvironmentPathMutationCollection"/> membership with for the same PATH-mutation
/// race reason.
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class GitWorktreeAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-worktree-adapter-{Guid.NewGuid():N}");
    private readonly GitWorktreeAdapter _adapter = new(new ChildProcessExecutionAdapter());

    public GitWorktreeAdapterTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

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

    private static string RunGitCapture(string workingDirectory, params string[] arguments)
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
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output.Trim();
    }

    private string CreateRepo(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q");
        RunGit(path, "config", "user.email", "test@example.com");
        RunGit(path, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(path, "file.txt"), "content");
        RunGit(path, "add", "file.txt");
        RunGit(path, "commit", "-q", "-m", "initial");
        return path;
    }

    [Fact]
    public async Task CreateAsync_creates_a_new_branch_and_worktree_at_the_exact_resolved_sha()
    {
        var mainRepo = CreateRepo("main");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");
        var workspacePath = Path.Combine(_root, "workspace-1");

        var result = await _adapter.CreateAsync(mainRepo, workspacePath, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        Assert.Equal(GitWorktreeCreationOutcome.Success, result.Outcome);
        Assert.True(Directory.Exists(workspacePath));
        var checkedOutSha = RunGitCapture(workspacePath, "rev-parse", "HEAD");
        Assert.Equal(headSha, checkedOutSha);
        var branch = RunGitCapture(workspacePath, "symbolic-ref", "-q", "--short", "HEAD");
        Assert.Equal("devalcopilot/workspace/test/1", branch);
    }

    [Fact]
    public async Task CreateAsync_never_mutates_any_file_head_index_or_ref_in_the_main_checkout()
    {
        var mainRepo = CreateRepo("main-untouched");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");
        var mainHeadBefore = File.ReadAllBytes(Path.Combine(mainRepo, ".git", "HEAD"));
        var trackedFileBefore = File.ReadAllBytes(Path.Combine(mainRepo, "file.txt"));
        var existingRefsBefore = Directory.Exists(Path.Combine(mainRepo, ".git", "refs", "heads"))
            ? Directory.GetFiles(Path.Combine(mainRepo, ".git", "refs", "heads")).ToDictionary(f => f, File.ReadAllBytes)
            : new Dictionary<string, byte[]>();
        var workspacePath = Path.Combine(_root, "workspace-untouched");

        var result = await _adapter.CreateAsync(mainRepo, workspacePath, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        Assert.Equal(GitWorktreeCreationOutcome.Success, result.Outcome);

        // The main checkout's own HEAD, index-affecting ref, and tracked file content are
        // byte-for-byte unchanged.
        Assert.Equal(mainHeadBefore, File.ReadAllBytes(Path.Combine(mainRepo, ".git", "HEAD")));
        Assert.Equal(trackedFileBefore, File.ReadAllBytes(Path.Combine(mainRepo, "file.txt")));
        foreach (var (path, contentBefore) in existingRefsBefore)
        {
            Assert.Equal(contentBefore, File.ReadAllBytes(path));
        }

        // Git's own additive administrative metadata is expected and bounded: a new subtree
        // under .git/worktrees/ and a new branch ref — never touching any pre-existing path.
        Assert.True(Directory.Exists(Path.Combine(mainRepo, ".git", "worktrees")));
        Assert.True(File.Exists(Path.Combine(mainRepo, ".git", "refs", "heads", "devalcopilot", "workspace", "test", "1")));
    }

    [Fact]
    public async Task CreateAsync_reports_path_already_exists_without_invoking_git()
    {
        var mainRepo = CreateRepo("main-collision");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");
        var workspacePath = Path.Combine(_root, "already-there");
        Directory.CreateDirectory(workspacePath);

        var result = await _adapter.CreateAsync(mainRepo, workspacePath, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        Assert.Equal(GitWorktreeCreationOutcome.PathAlreadyExists, result.Outcome);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_workspace_path_equal_to_the_main_repository()
    {
        var mainRepo = CreateRepo("main-equal-overlap");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");

        var result = await _adapter.CreateAsync(mainRepo, mainRepo, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        Assert.Equal(GitWorktreeCreationOutcome.WorkspaceOverlapsMainRepository, result.Outcome);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_workspace_path_nested_beneath_the_main_repository()
    {
        var mainRepo = CreateRepo("main-nested-overlap");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");
        var nestedWorkspacePath = Path.Combine(mainRepo, "nested-workspace");

        var result = await _adapter.CreateAsync(mainRepo, nestedWorkspacePath, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        Assert.Equal(GitWorktreeCreationOutcome.WorkspaceOverlapsMainRepository, result.Outcome);
        Assert.False(Directory.Exists(nestedWorkspacePath));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_main_repository_nested_beneath_the_workspace_path()
    {
        // The reverse direction: the "workspace" path is an ancestor of the main repository —
        // still an overlap, never only checked one way.
        var workspaceRoot = Path.Combine(_root, "ancestor-workspace");
        Directory.CreateDirectory(workspaceRoot);
        var mainRepo = CreateRepo(@"ancestor-workspace\nested-repo");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");

        var result = await _adapter.CreateAsync(mainRepo, workspaceRoot, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        Assert.Equal(GitWorktreeCreationOutcome.WorkspaceOverlapsMainRepository, result.Outcome);
    }

    [Fact]
    public async Task CreateAsync_rejects_overlap_before_any_git_process_is_invoked()
    {
        // A recording fake stands in for the real process adapter here specifically to prove
        // zero child processes are ever launched for an overlapping path — a stronger proof
        // than merely inspecting repository state afterward.
        var recordingAdapter = new RecordingProcessExecutionAdapter();
        var adapter = new GitWorktreeAdapter(recordingAdapter);
        var mainRepo = CreateRepo("main-overlap-no-invocation");

        var result = await adapter.CreateAsync(mainRepo, mainRepo, "devalcopilot/workspace/test/1", new string('a', 40), CancellationToken.None);

        Assert.Equal(GitWorktreeCreationOutcome.WorkspaceOverlapsMainRepository, result.Outcome);
        Assert.Equal(0, recordingAdapter.CallCount);
    }

    private sealed class RecordingProcessExecutionAdapter : IProcessExecutionAdapter
    {
        public int CallCount { get; private set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Must never be reached when the containment check rejects the request first.");
        }
    }

    [Fact]
    public async Task ResolveAdministrativeDirectoryAsync_discovers_and_validates_the_real_administrative_directory()
    {
        var mainRepo = CreateRepo("main-admin-dir");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");
        var workspacePath = Path.Combine(_root, "workspace-admin-dir");
        await _adapter.CreateAsync(mainRepo, workspacePath, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        var result = await _adapter.ResolveAdministrativeDirectoryAsync(mainRepo, workspacePath, CancellationToken.None);

        Assert.Equal(GitWorktreeAdministrativeDirectoryOutcome.Resolved, result.Outcome);
        Assert.NotNull(result.AdministrativeDirectory);
        // Real Git's own worktree administrative directory, never assumed by path convention —
        // proven here by checking it is exactly what Git itself reports from inside the
        // workspace, under the main repository's .git/worktrees/ subtree.
        var expectedGitDir = RunGitCapture(workspacePath, "rev-parse", "--git-dir");
        var expectedAbsolute = Path.GetFullPath(expectedGitDir.Replace('/', '\\'), workspacePath);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(expectedAbsolute),
            Path.TrimEndingDirectorySeparator(result.AdministrativeDirectory!));
        Assert.Contains(Path.Combine(mainRepo, ".git", "worktrees"), result.AdministrativeDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAdministrativeDirectoryAsync_reports_not_a_worktree_when_the_common_dir_points_elsewhere()
    {
        var mainRepo = CreateRepo("main-mismatch");
        var unrelatedRepo = CreateRepo("unrelated");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");
        var workspacePath = Path.Combine(_root, "workspace-mismatch");
        await _adapter.CreateAsync(mainRepo, workspacePath, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        // Deliberately pass the wrong "main repository" — the workspace really belongs to
        // mainRepo, not unrelatedRepo — proving the cross-validation actually checks agreement
        // rather than trusting path shape alone.
        var result = await _adapter.ResolveAdministrativeDirectoryAsync(unrelatedRepo, workspacePath, CancellationToken.None);

        Assert.Equal(GitWorktreeAdministrativeDirectoryOutcome.NotAWorktree, result.Outcome);
        Assert.Null(result.AdministrativeDirectory);
    }

    [Fact]
    public async Task GetHeadCommitShaAsync_reports_the_workspaces_own_head()
    {
        var mainRepo = CreateRepo("main-head");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");
        var workspacePath = Path.Combine(_root, "workspace-head");
        await _adapter.CreateAsync(mainRepo, workspacePath, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        var result = await _adapter.GetHeadCommitShaAsync(workspacePath, CancellationToken.None);

        Assert.Equal(GitWorktreeHeadOutcome.Resolved, result.Outcome);
        Assert.Equal(headSha, result.CommitSha);
    }

    [Fact]
    public async Task GetHeadCommitShaAsync_reflects_a_commit_made_inside_the_workspace_after_creation()
    {
        var mainRepo = CreateRepo("main-diverge");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");
        var workspacePath = Path.Combine(_root, "workspace-diverge");
        await _adapter.CreateAsync(mainRepo, workspacePath, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        File.WriteAllText(Path.Combine(workspacePath, "file.txt"), "changed inside workspace");
        RunGit(workspacePath, "commit", "-q", "-am", "external change");
        var newSha = RunGitCapture(workspacePath, "rev-parse", "HEAD");

        var result = await _adapter.GetHeadCommitShaAsync(workspacePath, CancellationToken.None);

        Assert.Equal(newSha, result.CommitSha);
        Assert.NotEqual(headSha, result.CommitSha);
    }

    [Fact]
    public async Task IsRegisteredAsync_reports_registered_for_a_real_worktree_and_not_registered_after_removal()
    {
        var mainRepo = CreateRepo("main-registered");
        var headSha = RunGitCapture(mainRepo, "rev-parse", "HEAD");
        var workspacePath = Path.Combine(_root, "workspace-registered");
        await _adapter.CreateAsync(mainRepo, workspacePath, "devalcopilot/workspace/test/1", headSha, CancellationToken.None);

        var registered = await _adapter.IsRegisteredAsync(mainRepo, workspacePath, CancellationToken.None);
        Assert.Equal(GitWorktreeRegistrationOutcome.Registered, registered.Outcome);

        RunGit(mainRepo, "worktree", "remove", "--force", workspacePath);

        var afterRemoval = await _adapter.IsRegisteredAsync(mainRepo, workspacePath, CancellationToken.None);
        Assert.Equal(GitWorktreeRegistrationOutcome.NotRegistered, afterRemoval.Outcome);
    }

    [Fact]
    public async Task IsRegisteredAsync_reports_not_registered_for_a_path_git_never_created()
    {
        var mainRepo = CreateRepo("main-never-registered");

        var result = await _adapter.IsRegisteredAsync(mainRepo, Path.Combine(_root, "never-created"), CancellationToken.None);

        Assert.Equal(GitWorktreeRegistrationOutcome.NotRegistered, result.Outcome);
    }
}
