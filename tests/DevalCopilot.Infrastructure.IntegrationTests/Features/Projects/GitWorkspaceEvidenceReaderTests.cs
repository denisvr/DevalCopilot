using System.Diagnostics;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>Exercises fingerprinting against a real Git repository. In particular, an
/// untracked file's content must alter the fingerprint even though porcelain status alone
/// would still merely say <c>?? path</c>.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class GitWorkspaceEvidenceReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-git-evidence-{Guid.NewGuid():N}");
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public GitWorkspaceEvidenceReaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureAsync_fingerprints_tracked_diff_and_untracked_file_content()
    {
        var repository = CreateRepository();
        File.WriteAllText(Path.Combine(repository, "tracked.txt"), "changed tracked content");
        var untrackedPath = Path.Combine(repository, "new-file.txt");
        File.WriteAllText(untrackedPath, "first untracked content");

        var first = await _reader.CaptureAsync(repository, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, first.Outcome);
        Assert.NotNull(first.FingerprintSha256);
        Assert.Contains(first.ChangedPaths, file => file.Path == "tracked.txt" && file.WorkTreeStatus == "M");
        Assert.Contains(first.ChangedPaths, file => file.Path == "new-file.txt" && file.IndexStatus == "?" && file.WorkTreeStatus == "?");
        Assert.Contains("changed tracked content", first.CompleteDiff!, StringComparison.Ordinal);

        File.WriteAllText(untrackedPath, "second untracked content");
        var second = await _reader.CaptureAsync(repository, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, second.Outcome);
        Assert.NotEqual(first.FingerprintSha256, second.FingerprintSha256);
    }

    private string CreateRepository()
    {
        var path = Path.Combine(_root, "repository");
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q");
        RunGit(path, "config", "user.email", "test@example.com");
        RunGit(path, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(path, "tracked.txt"), "original");
        RunGit(path, "add", "tracked.txt");
        RunGit(path, "commit", "-q", "-m", "initial");
        return path;
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
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
}
