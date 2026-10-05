using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>Real Git plus the real Windows filesystem: the capture a human checkpoint inspection derives its comparison from
/// (ADR-0027). It is the same coherent capture bracket and fingerprint as the ordinary and Agent captures, carries the attested
/// tracked facts, never the raw patch, instruction context or untracked previews, and does NOT reserve the two root instruction
/// names, while the Agent capture of the very same repository still does.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
[SupportedOSPlatform("windows")]
public sealed class GitWorkspaceCheckpointInspectionCaptureTests : IDisposable
{
    private const string Secret = "OUTSIDE-SECRET: bytes that must never reach an inspection capture.";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-inspection-capture-{Guid.NewGuid():N}");
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public GitWorkspaceCheckpointInspectionCaptureTests() => Directory.CreateDirectory(_root);

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

    [WindowsOnlyFact]
    public async Task The_inspection_capture_has_the_same_fingerprint_attests_tracked_text_and_carries_no_raw_patch()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "one\ntwo\n");
        Commit(repository, "gone.txt", "deleted\n");
        Write(repository, "a.txt", "one\nTWO\n");
        File.Delete(Path.Combine(repository, "gone.txt"));
        Write(repository, "untracked.txt", "untracked text\n");

        var ordinary = await _reader.CaptureAsync(repository, CancellationToken.None);
        var previews = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var agent = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);
        var inspection = await _reader.CaptureForCheckpointInspectionAsync(repository, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, inspection.Outcome);
        Assert.Equal(ordinary.FingerprintSha256, inspection.FingerprintSha256);
        Assert.Equal(previews.FingerprintSha256, inspection.FingerprintSha256);
        Assert.Equal(agent.FingerprintSha256, inspection.FingerprintSha256);
        Assert.Equal(ordinary.HeadCommitSha, inspection.HeadCommitSha);
        Assert.Equal(ordinary.ChangedPaths, inspection.ChangedPaths);
        Assert.Null(inspection.CompleteDiff);
        Assert.Null(inspection.UntrackedFiles);
        Assert.Null(inspection.InstructionContext);
        Assert.Equal(
            new GitWorkspaceTrackedFile[]
            {
                new("a.txt", null, "one\ntwo\n", "one\nTWO\n"),
                new("gone.txt", null, "deleted\n", null),
            },
            inspection.TrackedFiles);

        // The ordinary capture is untouched: it still carries Git's raw patch and no attestation.
        Assert.Contains("TWO", ordinary.CompleteDiff, StringComparison.Ordinal);
        Assert.Null(ordinary.TrackedFiles);
    }

    [WindowsOnlyTheory]
    [InlineData("AGENTS.md")]
    [InlineData("CLAUDE.md")]
    public async Task A_proven_root_instruction_file_is_inert_source_text_for_inspection_and_still_reserved_for_agents(string name)
    {
        var repository = CreateRepository();
        Commit(repository, name, "baseline instructions\n");
        Write(repository, name, "edited instructions\n");

        var inspection = await _reader.CaptureForCheckpointInspectionAsync(repository, CancellationToken.None);
        var agent = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: false, CancellationToken.None);

        Assert.Equal(
            new GitWorkspaceTrackedFile(name, null, "baseline instructions\n", "edited instructions\n"),
            Assert.Single(inspection.TrackedFiles!));
        Assert.Null(inspection.InstructionContext);
        Assert.Equal(
            GitWorkspaceTrackedFile.Omitted(name, GitWorkspaceTrackedOmission.ReservedInstructionFile),
            Assert.Single(agent.TrackedFiles!));
        Assert.NotNull(agent.InstructionContext);
        Assert.Equal(inspection.FingerprintSha256, agent.FingerprintSha256);
    }

    [WindowsOnlyTheory]
    [InlineData("AGENTS.md")]
    [InlineData("nested/other.txt")]
    public async Task A_tracked_file_that_has_a_second_name_outside_the_worktree_is_an_unproven_omission_beside_a_healthy_sibling(
        string path)
    {
        var repository = CreateRepository();
        Commit(repository, path, "baseline\n");
        Commit(repository, "healthy.txt", "before\n");
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, Secret);
        File.Delete(Path.Combine(repository, path.Replace('/', '\\')));
        HardLinkSupport.Create(Path.Combine(repository, path.Replace('/', '\\')), outside);
        Write(repository, "healthy.txt", "after\n");

        var result = await _reader.CaptureForCheckpointInspectionAsync(repository, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Equal(
            new[]
            {
                GitWorkspaceTrackedFile.Omitted(path, GitWorkspaceTrackedOmission.ContainmentUnproven),
                new GitWorkspaceTrackedFile("healthy.txt", null, "before\n", "after\n"),
            }.OrderBy(file => file.Path, StringComparer.Ordinal),
            result.TrackedFiles);
    }

    [WindowsOnlyFact]
    public async Task A_host_without_a_physical_containment_proof_attests_nothing_for_inspection()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "before\n");
        Write(repository, "a.txt", "after " + Secret + "\n");
        var noProof = new GitWorkspaceEvidenceReader(new ChildProcessExecutionAdapter(), physicalContainmentAvailable: false);

        var result = await noProof.CaptureForCheckpointInspectionAsync(repository, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(
            GitWorkspaceTrackedFile.Omitted("a.txt", GitWorkspaceTrackedOmission.ContainmentUnproven),
            Assert.Single(result.TrackedFiles!));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Null(result.CompleteDiff);
    }

    [WindowsOnlyFact]
    public async Task A_tracked_file_that_keeps_changing_during_the_inspection_capture_discards_the_whole_capture()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "before\n");
        Write(repository, "a.txt", "after 0\n");
        var counter = 0;
        var adapter = new MutatingAdapter(
            new ChildProcessExecutionAdapter(),
            request => request.Arguments.Contains("ls-tree"),
            () => Write(repository, "a.txt", $"after {++counter}\n"));

        var result = await new GitWorkspaceEvidenceReader(adapter).CaptureForCheckpointInspectionAsync(
            repository, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.TrackedFiles);
        Assert.Null(result.CompleteDiff);
    }

    private string CreateRepository()
    {
        var path = Path.Combine(_root, "repository");
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "Test");
        Git(path, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(path, "seed.txt"), "seed\n");
        Git(path, "add", "seed.txt");
        Git(path, "commit", "-q", "-m", "initial");
        return path;
    }

    private static void Commit(string repository, string relativePath, string content)
    {
        Write(repository, relativePath, content);
        Git(repository, "add", relativePath);
        Git(repository, "commit", "-q", "-m", "add " + relativePath);
    }

    private static void Write(string repository, string relativePath, string content)
    {
        var path = Path.Combine(repository, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
    }

    private static void Git(string workingDirectory, params string[] arguments) =>
        GitWorkspaceUntrackedPreviewTests.RunGit(workingDirectory, arguments);

    /// <summary>Runs the real adapter, then performs one side effect after every request the trigger selects.</summary>
    private sealed class MutatingAdapter(IProcessExecutionAdapter inner, Func<ProcessExecutionRequest, bool> trigger, Action action)
        : IProcessExecutionAdapter
    {
        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(request, cancellationToken);
            if (trigger(request))
            {
                action();
            }

            return result;
        }
    }
}
