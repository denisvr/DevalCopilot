using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The defect this slice closes, reproduced against two real, unrelated project worktrees. The expected
/// conventions are literals written here, independent of anything the production writer produces: an Agent claimed
/// for project Alpha must see Alpha's own root instructions, and must never be pointed at another project's
/// documentation names.</summary>
public sealed class ProjectInstructionContextRegressionTests : IDisposable
{
    private const string AlphaConvention = "ALPHA-CONVENTION: name every handler <Operation>Handler.";
    private const string BetaConvention = "BETA-CONVENTION: prefer small modules over classes.";
    private static readonly Guid Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-instruction-regression-{Guid.NewGuid():N}");

    public ProjectInstructionContextRegressionTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_planning_manifest_for_project_alpha_carries_alphas_own_root_instructions()
    {
        var alpha = CreateProject("alpha", AlphaConvention);
        var evidence = await new GitWorkspaceEvidenceReader(new ChildProcessExecutionAdapter())
            .CaptureForAgentContextAsync(alpha, true, CancellationToken.None);

        var manifest = ContextManifestBuilder.Build(
            Id, Id, Id, evidence.FingerprintSha256!, "objective", null, [], Instructions(evidence));

        Assert.Equal(AlphaConvention, AgentsText(manifest));
    }

    [Fact]
    public async Task A_review_manifest_for_project_beta_carries_betas_text_and_never_alphas()
    {
        var alpha = CreateProject("alpha", AlphaConvention);
        var beta = CreateProject("beta", BetaConvention);
        var reader = new GitWorkspaceEvidenceReader(new ChildProcessExecutionAdapter());
        _ = await reader.CaptureForAgentContextAsync(alpha, true, CancellationToken.None);
        var evidence = await reader.CaptureForAgentContextAsync(beta, true, CancellationToken.None);

        var manifest = ClaudeCriticalReviewContextManifestBuilder.Build(
            Id, Id, Id, evidence.FingerprintSha256!, "objective", Id, "summary", "{}", evidence.ChangedPaths,
            TrackedChangeEvidence.From(evidence), Instructions(evidence), evidence.UntrackedFiles);

        Assert.Equal(BetaConvention, AgentsText(manifest));
        Assert.DoesNotContain("ALPHA-CONVENTION", manifest, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/engineering-context.md")]
    [InlineData("docs/architecture/agent-collaboration-protocol.md")]
    public async Task A_manifest_for_an_unrelated_project_never_names_another_projects_documentation(string foreignReference)
    {
        var beta = CreateProject("beta", BetaConvention);
        var evidence = await new GitWorkspaceEvidenceReader(new ChildProcessExecutionAdapter())
            .CaptureForAgentContextAsync(beta, true, CancellationToken.None);

        var planning = ContextManifestBuilder.Build(
            Id, Id, Id, evidence.FingerprintSha256!, "objective", null, [], Instructions(evidence));
        var review = ClaudeCriticalReviewContextManifestBuilder.Build(
            Id, Id, Id, evidence.FingerprintSha256!, "objective", Id, "summary", "{}", evidence.ChangedPaths,
            TrackedChangeEvidence.From(evidence), Instructions(evidence), evidence.UntrackedFiles);

        Assert.DoesNotContain(foreignReference, planning, StringComparison.Ordinal);
        Assert.DoesNotContain(foreignReference, review, StringComparison.Ordinal);
    }

    private static string? AgentsText(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        return document.RootElement.GetProperty("projectInstructionContext").GetProperty("sources")[0].GetProperty("text").GetString();
    }

    private static ProjectInstructionContextManifest Instructions(GitWorkspaceEvidenceResult evidence) =>
        ProjectInstructionContextManifest.Prepare(Id, Id, evidence.FingerprintSha256!, evidence.InstructionContext);

    private string CreateProject(string name, string convention)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "Test");
        Git(path, "config", "core.autocrlf", "false");
        File.WriteAllBytes(Path.Combine(path, "AGENTS.md"), new UTF8Encoding(false).GetBytes(convention));
        Git(path, "add", "-A");
        Git(path, "commit", "-q", "-m", "initial");
        return path;
    }

    private static void Git(string workingDirectory, params string[] arguments)
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
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
