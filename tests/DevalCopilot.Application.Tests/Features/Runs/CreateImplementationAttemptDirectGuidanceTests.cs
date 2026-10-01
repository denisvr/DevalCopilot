using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Optional direct human guidance on the explicit initial implementation request, against a real SQLite file.</summary>
public sealed partial class CreateImplementationAttemptCommandHandlerTests
{
    private const string DirectSentinelGuidance = "SENTINEL-IMPL-3 Reuse the existing helper.";

    private CreateImplementationAttemptCommandHandler DirectGuidanceHandler(
        DevalCopilotDbContext context, FakeArtifactStore? store = null, IGitWorkspaceEvidenceReader? reader = null) =>
        new(context, reader ?? FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), store ?? new FakeArtifactStore(), new FixedTimeProvider(Now));

    private static async Task<string> ReadSealedManifestAsync(FakeArtifactStore store, Guid runId, Guid attemptId) =>
        await File.ReadAllTextAsync(store.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest));

    private async Task<string?> ReadStoredDirectGuidanceAsync(Guid attemptId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Database
            .SqlQuery<string?>($"SELECT AgentDirectHumanGuidance AS Value FROM attempts WHERE Id = {attemptId}")
            .SingleAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_guided_claim_snapshots_the_normalized_text_and_seals_it_once_for_both_plan_forms(bool revisedForm)
    {
        var (runId, proposalId) = await SeedTurnLimitScenarioAsync(initialLimit: null, revisedForm);
        var store = new FakeArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await DirectGuidanceHandler(context, store).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId, $"  {DirectSentinelGuidance.Replace("Reuse", "Reuse\r\n", StringComparison.Ordinal)}  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var attemptId = result.Value.AttemptId;
        var expected = DirectSentinelGuidance.Replace("Reuse", "Reuse\n", StringComparison.Ordinal);
        Assert.Equal(expected, await ReadStoredDirectGuidanceAsync(attemptId));
        Assert.Equal("claude-implementation-v2", await ClaudeMutationTurnLimitTestSupport.ReadAttemptContractVersionAsync(_fixture, attemptId));

        var manifest = await ReadSealedManifestAsync(store, runId, attemptId);
        Assert.True(DirectHumanGuidanceManifest.Agrees(manifest, expected));
        Assert.Equal(1, manifest.Split("SENTINEL-IMPL-3").Length - 1);
        Assert.Equal(DirectHumanGuidanceManifest.Boundary, JsonSerializer.Deserialize<JsonElement>(manifest).GetProperty("directHumanGuidanceBoundary").GetString());

        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId);
        Assert.Equal(DirectHumanGuidanceEvidence.Provided, attempt.GetDirectHumanGuidanceEvidence());
        Assert.Equal(expected, attempt.ReadAgentDirectHumanGuidance().Text);
        // No HumanInstruction, escalation, or authorization side effect: direct guidance is not an authorization.
        Assert.Empty(await verify.CollaborationMessages.Where(m => m.RunId == runId && m.Type == CollaborationMessageType.HumanInstruction).ToListAsync());
        Assert.Empty(await verify.ReviewCorrectionAuthorizations.ToListAsync());
    }

    [Fact]
    public async Task A_guided_claim_keeps_the_exact_ordered_inputs_of_the_unguided_claim()
    {
        var (runId, proposalId) = await SeedTurnLimitScenarioAsync(initialLimit: null);
        await using var context = _fixture.CreateContext();

        var result = await DirectGuidanceHandler(context).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId, DirectSentinelGuidance), CancellationToken.None);

        await using var verify = _fixture.CreateContext();
        var acceptanceId = await verify.CollaborationMessages
            .Where(m => m.RunId == runId && m.Type == CollaborationMessageType.Acceptance && m.InReplyToMessageId == proposalId)
            .Select(m => m.Id)
            .SingleAsync();
        var inputs = await verify.AttemptInputMessages.Where(m => m.AttemptId == result.Value.AttemptId).OrderBy(m => m.Sequence).ToListAsync();
        Assert.Equal([proposalId, acceptanceId], inputs.Select(m => m.CollaborationMessageId));
        Assert.Equal([0, 1], inputs.Select(m => m.Sequence));
        Assert.Equal(1, await verify.Artifacts.CountAsync(a => a.AttemptId == result.Value.AttemptId && a.Purpose == ArtifactPurpose.AgentContextManifest));
    }

    [Fact]
    public async Task An_unguided_claim_records_no_snapshot_and_seals_neither_member()
    {
        var (runId, proposalId) = await SeedTurnLimitScenarioAsync(initialLimit: 4);
        var store = new FakeArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await DirectGuidanceHandler(context, store).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await ReadStoredDirectGuidanceAsync(result.Value.AttemptId));
        var manifest = await ReadSealedManifestAsync(store, runId, result.Value.AttemptId);
        Assert.True(DirectHumanGuidanceManifest.Agrees(manifest, null));
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal(DirectHumanGuidanceEvidence.NotRecorded, attempt.GetDirectHumanGuidanceEvidence());
        Assert.Equal(4, attempt.AgentRequestedMaxTurns);
    }

    public static IEnumerable<object[]> InvalidGuidance()
    {
        yield return [""];
        yield return ["   \r\n  "];
        yield return ["tab\there SENTINEL-INVALID"];
        yield return ["bell\u0007 SENTINEL-INVALID"];
        yield return ["SENTINEL-INVALID the password is x"];
        yield return ["SENTINEL-INVALID see C:\\Users\\me"];
        yield return [new string('x', 601) + " SENTINEL-INVALID"];
        yield return [new string(['a', '\ud800', 'b'])];
    }

    [Theory]
    [MemberData(nameof(InvalidGuidance))]
    public async Task Invalid_guidance_is_refused_before_any_read_or_external_work_and_is_never_echoed(string guidance)
    {
        var (runId, proposalId) = await SeedTurnLimitScenarioAsync(initialLimit: null);
        var attemptsBefore = await AttemptCountAsync(runId);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, runId);
        var store = new FakeArtifactStore();
        var reader = new CountingEvidenceReader();
        await using var context = _fixture.CreateContext();

        var result = await DirectGuidanceHandler(context, store, reader).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId, guidance), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.direct_guidance_invalid", error.Code);
        Assert.DoesNotContain("SENTINEL", error.Description, StringComparison.Ordinal);
        Assert.Equal(0, reader.Calls);
        Assert.Empty(store.DeletedSealedFiles);
        await AssertNoImplementationClaimPersistedAsync(runId, attemptsBefore, eventsBefore);
    }

    [Fact]
    public async Task The_validator_rejects_invalid_guidance_with_a_fixed_non_echoing_message_and_accepts_null_and_valid_text()
    {
        var validator = new CreateImplementationAttemptCommandValidator();

        Assert.True((await validator.ValidateAsync(new CreateImplementationAttemptCommand(Guid.NewGuid(), Guid.NewGuid()))).IsValid);
        Assert.True((await validator.ValidateAsync(new CreateImplementationAttemptCommand(Guid.NewGuid(), Guid.NewGuid(), DirectSentinelGuidance))).IsValid);
        var invalid = await validator.ValidateAsync(new CreateImplementationAttemptCommand(Guid.NewGuid(), Guid.NewGuid(), "   "));
        Assert.False(invalid.IsValid);
        var failure = Assert.Single(invalid.Errors);
        Assert.Equal("agent_attempts.direct_guidance_invalid", failure.ErrorCode);
    }

    [Fact]
    public async Task Guidance_does_not_bypass_the_existing_admission_gates()
    {
        var (runId, proposalId) = await SeedTurnLimitScenarioAsync(initialLimit: null, maximumAgentAttempts: 1);
        await using var context = _fixture.CreateContext();
        var attemptsBefore = await AttemptCountAsync(runId);

        var exhausted = await DirectGuidanceHandler(context).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId, DirectSentinelGuidance), CancellationToken.None);
        var unknown = await DirectGuidanceHandler(context).HandleAsync(
            new CreateImplementationAttemptCommand(Guid.NewGuid(), proposalId, DirectSentinelGuidance), CancellationToken.None);

        Assert.Equal("agent_attempts.budget_exhausted", Assert.Single(exhausted.Errors).Code);
        Assert.Equal("runs.not_found", Assert.Single(unknown.Errors).Code);
        Assert.Equal(attemptsBefore, await AttemptCountAsync(runId));
    }

    [Fact]
    public async Task An_unresolved_plan_is_refused_exactly_as_without_guidance_and_records_nothing()
    {
        var (runId, _) = await SeedTurnLimitScenarioAsync(initialLimit: null);
        var otherProposal = Guid.NewGuid();

        var attemptsBefore = await AttemptCountAsync(runId);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, runId);
        var store = new FakeArtifactStore();
        await using var context = _fixture.CreateContext();

        var plain = await DirectGuidanceHandler(context, store).HandleAsync(
            new CreateImplementationAttemptCommand(runId, otherProposal), CancellationToken.None);
        var guided = await DirectGuidanceHandler(context, store).HandleAsync(
            new CreateImplementationAttemptCommand(runId, otherProposal, DirectSentinelGuidance), CancellationToken.None);

        Assert.Equal(Assert.Single(plain.Errors).Code, Assert.Single(guided.Errors).Code);
        await AssertNoImplementationClaimPersistedAsync(runId, attemptsBefore, eventsBefore);
    }

    [Fact]
    public async Task A_second_claim_while_the_guided_attempt_runs_is_refused_and_leaves_the_first_snapshot_untouched()
    {
        var (runId, proposalId) = await SeedTurnLimitScenarioAsync(initialLimit: null);
        await using var firstContext = _fixture.CreateContext();
        var first = await DirectGuidanceHandler(firstContext).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId, DirectSentinelGuidance), CancellationToken.None);
        await using var secondContext = _fixture.CreateContext();

        var second = await DirectGuidanceHandler(secondContext).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId, "SENTINEL-OTHER another text"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal("attempts.run_has_active_attempt", Assert.Single(second.Errors).Code);
        Assert.Equal(DirectSentinelGuidance, await ReadStoredDirectGuidanceAsync(first.Value.AttemptId));
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.Implementer).ToListAsync());
    }

    [Fact]
    public async Task A_commit_race_rolls_back_the_guided_claim_completely_and_removes_the_orphan_manifest()
    {
        var (runId, proposalId) = await SeedTurnLimitScenarioAsync(initialLimit: 5);
        var attemptsBefore = await AttemptCountAsync(runId);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, runId);
        var store = new FakeArtifactStore();
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, runId, 6)));

        var result = await DirectGuidanceHandler(context, store).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId, DirectSentinelGuidance), CancellationToken.None);

        Assert.Equal("agent_attempts.run_changed_during_claim", Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await AssertNoImplementationClaimPersistedAsync(runId, attemptsBefore, eventsBefore);
    }

    [Fact]
    public async Task A_later_run_level_change_or_second_request_never_alters_the_recorded_snapshot_or_sealed_manifest()
    {
        var (runId, proposalId) = await SeedTurnLimitScenarioAsync(initialLimit: null);
        var store = new FakeArtifactStore();
        await using var context = _fixture.CreateContext();
        var result = await DirectGuidanceHandler(context, store).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId, DirectSentinelGuidance), CancellationToken.None);
        var manifestBefore = await ReadSealedManifestAsync(store, runId, result.Value.AttemptId);

        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, runId, 33);
        await using var again = _fixture.CreateContext();
        await DirectGuidanceHandler(again).HandleAsync(
            new CreateImplementationAttemptCommand(runId, proposalId, "SENTINEL-LATER"), CancellationToken.None);

        Assert.Equal(DirectSentinelGuidance, await ReadStoredDirectGuidanceAsync(result.Value.AttemptId));
        Assert.Equal(manifestBefore, await ReadSealedManifestAsync(store, runId, result.Value.AttemptId));
    }

    private sealed class CountingEvidenceReader : IGitWorkspaceEvidenceReader
    {
        public int Calls { get; private set; }

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null));
        }
    }
}
