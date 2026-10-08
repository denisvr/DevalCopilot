using System.Diagnostics;
using System.Security.Cryptography;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// A real main repository with one baseline commit, a real linked worktree created by the production
/// <see cref="GitWorktreeAdapter"/>, a real ownership marker, and the production <see cref="LocalCommitGit"/> composed over the
/// real child-process adapter and evidence reader. Nothing about Git is doubled; only the identifiers are chosen by the test.
/// </summary>
internal sealed class LocalCommitScene : IDisposable
{
    public const string Message = "Deliver the approved change";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-local-commit-{Guid.NewGuid():N}");
    private readonly ChildProcessExecutionAdapter _process = new();

    public LocalCommitScene()
    {
        Directory.CreateDirectory(_root);
        MainPath = Path.Combine(_root, "main");
        WorkspacePath = Path.Combine(_root, "workspace");
        BranchName = "devalcopilot/workspace/test/1";
        Storage = new LocalCommitStorage(Path.Combine(_root, "local-commit"));
        Worktrees = new GitWorktreeAdapter(_process);
        Markers = new WorkspaceOwnershipMarkerStore();
        Evidence = new GitWorkspaceEvidenceReader(_process);
        Git = new LocalCommitGit(_process, Worktrees, Markers, Storage);

        Directory.CreateDirectory(MainPath);
        RunGit(MainPath, "init", "-q");
        RunGit(MainPath, "config", "user.email", "owner@example.com");
        RunGit(MainPath, "config", "user.name", "Local Owner");
        RunGit(MainPath, "config", "core.autocrlf", "false");
        Write("a.txt", "a\n");
        Write("d.txt", "to delete\n");
        Write("sub/q.txt", "q\n");
        Write("run.sh", "#!/bin/sh\necho hi\n");
        RunGit(MainPath, "add", "-A");
        RunGit(MainPath, "update-index", "--chmod=+x", "run.sh");
        RunGit(MainPath, "commit", "-q", "-m", "baseline");
        BaselineCommit = RunGit(MainPath, "rev-parse", "HEAD").Trim();

        var created = Worktrees.CreateAsync(MainPath, WorkspacePath, BranchName, BaselineCommit, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(GitWorktreeCreationOutcome.Success, created.Outcome);
        var administrative = Worktrees.ResolveAdministrativeDirectoryAsync(MainPath, WorkspacePath, CancellationToken.None).GetAwaiter().GetResult();
        AdministrativeDirectory = administrative.AdministrativeDirectory!.Replace('/', '\\');
        CommonDirectory = administrative.CommonDirectory!.Replace('/', '\\');

        Ownership = new LocalCommitOwnership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42UL, "00112233445566778899AABBCCDDEEFF");
        var marker = new WorkspaceOwnershipMarker(
            Ownership.WorkspaceId, Ownership.ProjectId, Ownership.LeaseId, Ownership.PhysicalVolumeSerialNumber, Ownership.PhysicalFileIdHex);
        Assert.Equal(
            WorkspaceOwnershipMarkerWriteOutcome.Success,
            Markers.WriteAsync(AdministrativeDirectory, marker, CancellationToken.None).GetAwaiter().GetResult().Outcome);
    }

    public string Root => _root;

    /// <summary>The durable rows the host seeds carry their own identifiers; the worktree's marker is rewritten to match them.</summary>
    public void AdoptOwnership(Guid workspaceId, Guid projectId, Guid leaseId)
    {
        Ownership = Ownership with { WorkspaceId = workspaceId, ProjectId = projectId, LeaseId = leaseId };
        var marker = new WorkspaceOwnershipMarker(
            workspaceId, projectId, leaseId, Ownership.PhysicalVolumeSerialNumber, Ownership.PhysicalFileIdHex);
        Assert.Equal(
            WorkspaceOwnershipMarkerWriteOutcome.Success,
            Markers.WriteAsync(AdministrativeDirectory, marker, CancellationToken.None).GetAwaiter().GetResult().Outcome);
    }

    public string MainPath { get; }

    public string WorkspacePath { get; }

    public string BranchName { get; }

    public string BaselineCommit { get; }

    public string AdministrativeDirectory { get; }

    public string CommonDirectory { get; }

    public LocalCommitOwnership Ownership { get; private set; }

    public LocalCommitStorage Storage { get; }

    /// <summary>The directory that holds the artifact an operation recorded (its own leaf, or a legacy operation-named directory).</summary>
    public string ArtifactLeaf(string relativePath) => Path.GetDirectoryName(Storage.ResolveArtifact(relativePath)!)!;

    /// <summary>Every first-level directory under the host-owned work and operations folders: the scratch and artifact leaves.</summary>
    public string[] StorageLeaves() =>
        new[] { "work", "operations" }
            .Select(folder => Path.Combine(Storage.Root, folder))
            .Where(Directory.Exists)
            .SelectMany(Directory.GetDirectories)
            .ToArray();

    public GitWorktreeAdapter Worktrees { get; }

    public WorkspaceOwnershipMarkerStore Markers { get; }

    public GitWorkspaceEvidenceReader Evidence { get; }

    public LocalCommitGit Git { get; }

    public string IndexPath => Path.Combine(AdministrativeDirectory, "index");

    public void Write(string relativePath, string content, bool inWorkspace = false)
    {
        var path = Path.Combine(inWorkspace ? WorkspacePath : MainPath, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void WriteWorkspace(string relativePath, string content) => Write(relativePath, content, inWorkspace: true);

    public void WriteWorkspaceBytes(string relativePath, byte[] content)
    {
        var path = Path.Combine(WorkspacePath, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    /// <summary>The ordinary approved-checkpoint observation: exactly what a checkpoint records.</summary>
    public async Task<(string Fingerprint, IReadOnlyList<LocalCommitChangedPath> Changes)> CaptureAsync()
    {
        var capture = await Evidence.CaptureAsync(WorkspacePath, CancellationToken.None);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, capture.Outcome);
        return (
            capture.FingerprintSha256!,
            capture.ChangedPaths.Select(path => new LocalCommitChangedPath(path.Path, path.IndexStatus, path.WorkTreeStatus)).ToArray());
    }

    public async Task<(LocalCommitPreparationResult Result, LocalCommitPreparationRequest Request)> PrepareAsync(
        Guid? operationId = null, string? fingerprintOverride = null, IReadOnlyList<LocalCommitChangedPath>? changesOverride = null)
    {
        var (fingerprint, changes) = fingerprintOverride is not null && changesOverride is not null
            ? (fingerprintOverride, changesOverride)
            : await CaptureAsync();
        var request = new LocalCommitPreparationRequest(
            operationId ?? Guid.NewGuid(),
            MainPath,
            WorkspacePath,
            BranchName,
            BaselineCommit,
            fingerprintOverride ?? fingerprint,
            changesOverride ?? changes,
            Message,
            Ownership,
            DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        return (await Git.PrepareAsync(request, CancellationToken.None), request);
    }

    public LocalCommitFacts FactsFor(LocalCommitPreparationRequest request, LocalCommitPreparedFacts prepared) => new(
        request.OperationId,
        MainPath,
        WorkspacePath,
        BranchName,
        BaselineCommit,
        prepared.TreeSha,
        prepared.CommitSha,
        LocalCommitMessagePolicy.BuildCommitMessage(request.NormalizedMessage, request.OperationId),
        prepared.AuthorName,
        prepared.AuthorEmail,
        prepared.CommitTimeUnixSeconds,
        prepared.IndexPreimageSha256,
        prepared.PreparedIndexSha256,
        prepared.PreparedIndexRelativePath,
        Ownership);

    public string MainRepositoryFingerprint()
    {
        var head = File.ReadAllText(Path.Combine(MainPath, ".git", "HEAD"));
        var branch = RunGit(MainPath, "rev-parse", "refs/heads/" + RunGit(MainPath, "symbolic-ref", "--short", "HEAD").Trim()).Trim();
        var index = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(MainPath, ".git", "index"))));
        var files = string.Join(
            '|',
            Directory.EnumerateFiles(MainPath, "*", SearchOption.AllDirectories)
                .Where(path => !path.Contains("\\.git\\", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => path + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
        return $"{head}|{branch}|{index}|{files}";
    }

    public string RunWorkspaceGit(params string[] arguments) => RunGit(WorkspacePath, arguments);

    public string RunMainGit(params string[] arguments) => RunGit(MainPath, arguments);

    /// <summary>Runs ordinary Git and returns its exit code instead of asserting success, for an external writer that is
    /// expected to be refused (a held lock, a held handle).</summary>
    public (int ExitCode, string Output, string Error) RunGitRaw(string workingDirectory, params string[] arguments)
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
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result, error.Result);
    }

    public static string RunGit(string workingDirectory, params string[] arguments)
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
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error.Result}");
        return output.Result;
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
}
