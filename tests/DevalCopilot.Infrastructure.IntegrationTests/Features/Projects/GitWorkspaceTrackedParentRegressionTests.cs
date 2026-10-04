using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>Written first against the parent: a tracked file that became a hard link to an outside file must never put the
/// outside bytes into the capture a new Agent claim seals. The parent returns the raw working-path patch, so these fail there.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class GitWorkspaceTrackedParentRegressionTests : IDisposable
{
    private const string OutsideSecret = "TOP SECRET TEXT FROM OUTSIDE THE WORKTREE";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-tracked-{Guid.NewGuid():N}");
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public GitWorkspaceTrackedParentRegressionTests() => Directory.CreateDirectory(_root);

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

    [WindowsOnlyFact]
    public async Task A_tracked_file_replaced_by_a_hard_link_to_an_outside_file_never_puts_the_outside_bytes_in_the_agent_capture()
    {
        var repository = CreateRepository();
        var outside = Path.Combine(_root, "outside-secret.txt");
        File.WriteAllText(outside, OutsideSecret);
        File.Delete(Path.Combine(repository, "linked.txt"));
        HardLinkSupport.Create(Path.Combine(repository, "linked.txt"), outside);
        File.Delete(Path.Combine(repository, "deep", "nested", "linked.txt"));
        HardLinkSupport.Create(Path.Combine(repository, "deep", "nested", "linked.txt"), outside);
        File.WriteAllText(Path.Combine(repository, "safe.txt"), "safe sibling after\n");

        var ordinary = await _reader.CaptureAsync(repository, CancellationToken.None);
        var result = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        // The parent defect's precondition: ordinary raw observation reads the named outside path.
        Assert.Contains(OutsideSecret, ordinary.CompleteDiff!, StringComparison.Ordinal);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(ordinary.FingerprintSha256, result.FingerprintSha256);
        Assert.DoesNotContain(OutsideSecret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [WindowsOnlyFact]
    public async Task A_tracked_file_with_a_second_name_inside_the_worktree_never_puts_its_bytes_in_the_agent_capture()
    {
        var repository = CreateRepository();
        File.WriteAllText(Path.Combine(repository, "safe.txt"), "two names inside the worktree " + OutsideSecret + "\n");
        HardLinkSupport.Create(Path.Combine(repository, "alias.txt"), Path.Combine(repository, "safe.txt"));

        var result = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: false, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.DoesNotContain(OutsideSecret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private string CreateRepository()
    {
        var path = Path.Combine(_root, "repository");
        Directory.CreateDirectory(Path.Combine(path, "deep", "nested"));
        GitWorkspaceUntrackedPreviewTests.RunGit(path, "init", "-q");
        GitWorkspaceUntrackedPreviewTests.RunGit(path, "config", "user.email", "test@example.com");
        GitWorkspaceUntrackedPreviewTests.RunGit(path, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(path, "linked.txt"), "committed root\n");
        File.WriteAllText(Path.Combine(path, "deep", "nested", "linked.txt"), "committed nested\n");
        File.WriteAllText(Path.Combine(path, "safe.txt"), "safe sibling before\n");
        GitWorkspaceUntrackedPreviewTests.RunGit(path, "add", "-A");
        GitWorkspaceUntrackedPreviewTests.RunGit(path, "commit", "-q", "-m", "initial");
        return path;
    }
}
