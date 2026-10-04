using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class CreateCodexPlanningAttemptCommandHandlerTests
{
    /// <summary>Answers only the Agent-context capture, recording whether untracked previews were requested, and fails any
    /// other kind of capture so a planning claim that bypassed instruction capture cannot pass.</summary>
    private sealed class InstructionAwareEvidenceReader(string fingerprint, GitWorkspaceInstructionContext? instructions)
        : IGitWorkspaceEvidenceReader
    {
        public List<bool> AgentContextCaptures { get; } = [];

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A planning claim must capture through the Agent-context capture.");

        public Task<GitWorkspaceEvidenceResult> CaptureForAgentContextAsync(
            string workspacePath, bool includeUntrackedPreviews, CancellationToken cancellationToken)
        {
            AgentContextCaptures.Add(includeUntrackedPreviews);
            return Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprint, [], null, null, instructions));
        }
    }

    [Fact]
    public async Task HandleAsync_seals_the_projects_own_root_instructions_without_asking_for_untracked_previews()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var reader = new InstructionAwareEvidenceReader(Fingerprint, InstructionContextTestSupport.Delivered);
        var store = new FakeArtifactStore();

        var result = await new CreateCodexPlanningAttemptCommandHandler(
            dbContext, reader, store, new FixedTimeProvider(Now), DurabilityProbe)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([false], reader.AgentContextCaptures);
        var manifest = File.ReadAllText(store.GetPartialPath(run.Id, result.Value.AttemptId, ArtifactPurpose.AgentContextManifest));
        using var document = JsonDocument.Parse(manifest);
        InstructionContextTestSupport.AssertManifestCarriesDeliveredInstructions(document.RootElement);
        Assert.Equal(workspace.Id, document.RootElement.GetProperty("projectInstructionContext").GetProperty("sourceGitWorkspaceId").GetGuid());
        Assert.Equal(checkpoint.Id, document.RootElement.GetProperty("projectInstructionContext").GetProperty("sourceGitCheckpointId").GetGuid());
        Assert.DoesNotContain("docs/engineering-context.md", manifest, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(manifest) <= 32 * 1024);
    }

    [Fact]
    public async Task HandleAsync_reports_both_files_as_not_captured_when_the_reader_returns_no_instruction_context()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);
        var store = new FakeArtifactStore();

        var result = await new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), store, new FixedTimeProvider(Now), DurabilityProbe)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var manifest = File.ReadAllText(store.GetPartialPath(run.Id, result.Value.AttemptId, ArtifactPurpose.AgentContextManifest));
        using var document = JsonDocument.Parse(manifest);
        var sources = document.RootElement.GetProperty("projectInstructionContext").GetProperty("sources").EnumerateArray().ToArray();
        Assert.All(sources, source =>
        {
            Assert.Equal("Omitted", source.GetProperty("status").GetString());
            Assert.Equal("not_captured", source.GetProperty("reason").GetString());
            Assert.Equal(JsonValueKind.Null, source.GetProperty("text").ValueKind);
        });
        Assert.DoesNotContain("\"Absent\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_does_not_claim_when_the_capture_is_not_current_even_if_instructions_were_read()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);
        var store = new FakeArtifactStore();

        var result = await new CreateCodexPlanningAttemptCommandHandler(
            dbContext, new InstructionAwareEvidenceReader(new string('9', 64), InstructionContextTestSupport.Delivered), store,
            new FixedTimeProvider(Now), DurabilityProbe)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", result.Errors[0].Code);
        Assert.Empty(await dbContext.Attempts.Where(attempt => attempt.RunId == run.Id).ToListAsync());
        Assert.Empty(store.DeletedSealedFiles);
    }
}
