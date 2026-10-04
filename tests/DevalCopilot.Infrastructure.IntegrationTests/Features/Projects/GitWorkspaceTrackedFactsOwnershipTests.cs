using System.Collections;
using System.Runtime.Versioning;
using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>R1 of the attested tracked-change review against the REAL reader: the facts a successful new Agent capture returns are an
/// owned immutable snapshot. A caller that casts the returned collection to every mutable shape and tries to replace the attested text
/// after the physical proof changes nothing, and the same holds for the result a projection copies.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
[SupportedOSPlatform("windows")]
public sealed class GitWorkspaceTrackedFactsOwnershipTests : IDisposable
{
    private const string Forged = "FORGED TEXT NEVER ATTESTED";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-ownership-{Guid.NewGuid():N}");
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public GitWorkspaceTrackedFactsOwnershipTests() => Directory.CreateDirectory(_root);

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
    public async Task A_successful_capture_returns_facts_that_no_cast_can_replace_after_the_physical_proof()
    {
        var repository = CreateRepository();
        File.WriteAllText(Path.Combine(repository, "a.txt"), "before\n");
        Git(repository, "add", "a.txt");
        Git(repository, "commit", "-q", "-m", "add a");
        File.WriteAllText(Path.Combine(repository, "a.txt"), "after\n");

        var result = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: false, CancellationToken.None);
        var expected = new GitWorkspaceTrackedFile("a.txt", null, "before\n", "after\n");
        var forged = expected with { AfterText = Forged };
        var routes = new Action[]
        {
            () => ((IList<GitWorkspaceTrackedFile>)result.TrackedFiles!)[0] = forged,
            () => ((IList<GitWorkspaceTrackedFile>)result.TrackedFiles!).Add(forged),
            () => ((IList<GitWorkspaceTrackedFile>)result.TrackedFiles!).Clear(),
            () => ((List<GitWorkspaceTrackedFile>)result.TrackedFiles!)[0] = forged,
            () => ((GitWorkspaceTrackedFile[])result.TrackedFiles!)[0] = forged,
            () => ((IList)result.TrackedFiles!)[0] = forged,
        };
        foreach (var route in routes)
        {
            try
            {
                route();
            }
            catch (Exception exception) when (exception is InvalidCastException or NotSupportedException or ArgumentException)
            {
                // Refused, as required.
            }
        }

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(expected, Assert.Single(result.TrackedFiles!));
    }

    private string CreateRepository()
    {
        var path = Path.Combine(_root, "repository");
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "Test");
        Git(path, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(path, "seed.txt"), "seed\n", new UTF8Encoding(false));
        Git(path, "add", "seed.txt");
        Git(path, "commit", "-q", "-m", "initial");
        return path;
    }

    private static void Git(string workingDirectory, params string[] arguments) => GitWorkspaceUntrackedPreviewTests.RunGit(workingDirectory, arguments);
}
