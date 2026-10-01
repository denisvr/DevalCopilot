using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Optional direct human guidance on the ordinary review-correction request: within the ordinary budget only,
/// never changing the authorization or escalation flows that exist at exhaustion.</summary>
public sealed partial class CreateReviewCorrectionAttemptCommandHandlerTests
{
    private const string DirectCorrectionGuidance = "SENTINEL-CORR-5 Keep the fix inside the existing helper.";

    private static CreateReviewCorrectionAttemptCommand GuidedCommand(Seed seed, string? guidance) =>
        new(seed.Run.Id, seed.Review.Id, guidance);

    private async Task<string?> ReadDirectGuidanceAsync(Guid attemptId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Database
            .SqlQuery<string?>($"SELECT AgentDirectHumanGuidance AS Value FROM attempts WHERE Id = {attemptId}")
            .SingleAsync();
    }

    private static async Task<string> ReadManifestAsync(TestArtifactStore store, Guid runId, Guid attemptId) =>
        await File.ReadAllTextAsync(store.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest));

    private async Task<Seed> SeedExhaustedAsync(bool withEscalation)
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context, maximumAgentInvocationTime: TimeSpan.FromHours(24));
        for (var number = 5; number <= 6; number++)
        {
            var priorCorrection = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), seed.Run.Id, number, seed.Workspace.Id, seed.Checkpoint!.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, Now, number);
            priorCorrection.MarkAgentDispatched(Now);
            priorCorrection.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
            context.Attempts.Add(priorCorrection);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        if (withEscalation)
        {
            Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(
                (await Handler(context).HandleAsync(Command(seed), CancellationToken.None)).Value);
        }

        return seed;
    }

    private async Task AssertNoEscalationOrClaimAsync(Seed seed, int expectedEscalations, int expectedAttempts)
    {
        await using var verify = _fixture.CreateContext();
        Assert.Equal(expectedEscalations, await verify.ReviewCorrectionEscalations.CountAsync(e => e.RunId == seed.Run.Id));
        Assert.Equal(expectedEscalations, await verify.CollaborationMessages.CountAsync(m => m.RunId == seed.Run.Id && m.Type == CollaborationMessageType.Escalation));
        Assert.Equal(expectedAttempts, await verify.Attempts.CountAsync(a => a.RunId == seed.Run.Id));
        Assert.Empty(await verify.Artifacts.Where(a => a.RunId == seed.Run.Id && a.Purpose == ArtifactPurpose.AgentContextManifest).ToListAsync());
    }

    [Fact]
    public async Task A_guided_claim_within_the_ordinary_budget_snapshots_normalizes_and_seals_the_text_once()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var store = new TestArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence), store)
            .HandleAsync(GuidedCommand(seed, $"  {DirectCorrectionGuidance.Replace("Keep", "Keep\r\n", StringComparison.Ordinal)}  "), CancellationToken.None);

        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        var expected = DirectCorrectionGuidance.Replace("Keep", "Keep\n", StringComparison.Ordinal);
        Assert.Equal(expected, await ReadDirectGuidanceAsync(created.AttemptId));
        var manifest = await ReadManifestAsync(store, seed.Run.Id, created.AttemptId);
        Assert.True(DirectHumanGuidanceManifest.Agrees(manifest, expected));
        Assert.Equal(1, manifest.Split("SENTINEL-CORR-5").Length - 1);
        Assert.False(JsonSerializer.Deserialize<JsonElement>(manifest).TryGetProperty("humanGuidance", out _));

        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == created.AttemptId);
        Assert.Equal("claude-review-correction-v2", attempt.AgentAdapterContractVersion);
        Assert.Equal(DirectHumanGuidanceEvidence.Provided, attempt.GetDirectHumanGuidanceEvidence());
        var inputs = await verify.AttemptInputMessages.Where(m => m.AttemptId == created.AttemptId).OrderBy(m => m.Sequence).ToListAsync();
        Assert.Equal([seed.ExecutionReport.Id, .. seed.Findings.Select(f => f.Id)], inputs.Select(m => m.CollaborationMessageId));
        Assert.Empty(await verify.ReviewCorrectionEscalations.ToListAsync());
        Assert.Empty(await verify.ReviewCorrectionAuthorizations.ToListAsync());
        Assert.Empty(await verify.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.HumanInstruction).ToListAsync());
    }

    [Fact]
    public async Task An_unguided_claim_records_no_snapshot_and_seals_neither_member()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var store = new TestArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence), store)
            .HandleAsync(Command(seed), CancellationToken.None);

        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        Assert.Null(await ReadDirectGuidanceAsync(created.AttemptId));
        Assert.True(DirectHumanGuidanceManifest.Agrees(await ReadManifestAsync(store, seed.Run.Id, created.AttemptId), null));
        await using var verify = _fixture.CreateContext();
        Assert.Equal(
            DirectHumanGuidanceEvidence.NotRecorded,
            (await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == created.AttemptId)).GetDirectHumanGuidanceEvidence());
    }

    [Theory]
    [MemberData(nameof(InvalidDirectGuidance))]
    public async Task Invalid_guidance_is_refused_before_any_read_or_external_work_and_is_never_echoed(string guidance)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, seed.Run.Id);
        var captures = 0;
        var store = new TestArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence, _ => { captures++; return Task.CompletedTask; }), store)
            .HandleAsync(GuidedCommand(seed, guidance), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.direct_guidance_invalid", error.Code);
        Assert.DoesNotContain("SENTINEL", error.Description, StringComparison.Ordinal);
        Assert.Equal(0, captures);
        Assert.Empty(store.DeletedSealedFiles);
        await AssertNoCorrectionClaimPersistedAsync(seed, attemptsBefore, eventsBefore);
    }

    public static IEnumerable<object[]> InvalidDirectGuidance()
    {
        yield return [""];
        yield return ["  \r\n "];
        yield return ["tab\there SENTINEL-INVALID"];
        yield return ["bell\u0007 SENTINEL-INVALID"];
        yield return ["SENTINEL-INVALID the password is x"];
        yield return [new string('x', 601) + " SENTINEL-INVALID"];
        yield return [new string(['a', '\ud800', 'b'])];
    }

    [Fact]
    public async Task The_validator_rejects_invalid_guidance_with_a_fixed_code_and_accepts_null_and_valid_text()
    {
        var validator = new CreateReviewCorrectionAttemptCommandValidator();

        Assert.True((await validator.ValidateAsync(new CreateReviewCorrectionAttemptCommand(Guid.NewGuid(), Guid.NewGuid()))).IsValid);
        Assert.True((await validator.ValidateAsync(new CreateReviewCorrectionAttemptCommand(Guid.NewGuid(), Guid.NewGuid(), DirectCorrectionGuidance))).IsValid);
        var invalid = await validator.ValidateAsync(new CreateReviewCorrectionAttemptCommand(Guid.NewGuid(), Guid.NewGuid(), " "));
        Assert.Equal("agent_attempts.direct_guidance_invalid", Assert.Single(invalid.Errors).ErrorCode);
    }

    [Fact]
    public async Task At_exhaustion_guided_requests_are_refused_without_creating_an_escalation_even_the_first_time()
    {
        var seed = await SeedExhaustedAsync(withEscalation: false);
        await using var context = _fixture.CreateContext();
        var store = new TestArtifactStore();

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence), store)
            .HandleAsync(GuidedCommand(seed, DirectCorrectionGuidance), CancellationToken.None);

        AssertCode(result, "agent_attempts.direct_guidance_unavailable");
        Assert.DoesNotContain("SENTINEL", Assert.Single(result.Errors).Description, StringComparison.Ordinal);
        Assert.Empty(store.DeletedSealedFiles);
        await AssertNoEscalationOrClaimAsync(seed, expectedEscalations: 0, expectedAttempts: 6);
    }

    [Fact]
    public async Task At_exhaustion_a_guided_request_does_not_disturb_an_existing_escalation_and_the_unguided_retry_still_returns_it()
    {
        var seed = await SeedExhaustedAsync(withEscalation: true);
        await using var context = _fixture.CreateContext();

        var guided = await Handler(context).HandleAsync(GuidedCommand(seed, DirectCorrectionGuidance), CancellationToken.None);
        var plain = await Handler(context).HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(guided, "agent_attempts.direct_guidance_unavailable");
        Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(plain.Value);
        await AssertNoEscalationOrClaimAsync(seed, expectedEscalations: 1, expectedAttempts: 6);
    }

    [Fact]
    public async Task At_exhaustion_with_an_available_authorization_a_guided_request_is_refused_and_consumes_nothing()
    {
        var (seed, authorizationId) = await SeedAuthorizedAsync();
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var context = _fixture.CreateContext();

        var guided = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence), store, Now.AddMinutes(2))
            .HandleAsync(GuidedCommand(seed, DirectCorrectionGuidance), CancellationToken.None);

        AssertCode(guided, "agent_attempts.direct_guidance_unavailable");
        Assert.Empty(store.DeletedSealedFiles);
        await AssertNoCorrectionClaimPersistedAsync(seed, attemptsBefore, eventsBefore);
        await using (var verify = _fixture.CreateContext())
        {
            var authorization = await verify.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorizationId);
            Assert.Null(authorization.ConsumedByAttemptId);
            Assert.True(authorization.IsAvailable);
            Assert.Single(await verify.ReviewCorrectionEscalations.ToListAsync());
        }

        // The positive control: the existing unguided authorized claim still works and consumes it exactly once,
        // carrying the authorization's own humanGuidance semantics and no direct snapshot.
        var plainStore = new TestArtifactStore();
        await using var plainContext = _fixture.CreateContext();
        var plain = await StopHandler(plainContext, new RecordingEvidenceReader(seed.Evidence), plainStore, Now.AddMinutes(2))
            .HandleAsync(Command(seed), CancellationToken.None);
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(plain.Value);
        Assert.Null(await ReadDirectGuidanceAsync(created.AttemptId));
        Assert.True(DirectHumanGuidanceManifest.Agrees(await ReadManifestAsync(plainStore, seed.Run.Id, created.AttemptId), null));
        await using var after = _fixture.CreateContext();
        Assert.Equal(created.AttemptId, (await after.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorizationId)).ConsumedByAttemptId);
    }

    [Fact]
    public async Task A_commit_race_rolls_back_the_guided_claim_completely_and_removes_the_orphan_manifest()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, 5);
        var attemptsBefore = await StopAttemptCountAsync(seed.Run.Id);
        var eventsBefore = await ClaudeMutationTurnLimitTestSupport.CountEventsAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, seed.Run.Id, 6)));

        var result = await StopHandler(context, new RecordingEvidenceReader(seed.Evidence), store)
            .HandleAsync(GuidedCommand(seed, DirectCorrectionGuidance), CancellationToken.None);

        AssertCode(result, "agent_attempts.run_changed_during_claim");
        Assert.Single(store.DeletedSealedFiles);
        await AssertNoCorrectionClaimPersistedAsync(seed, attemptsBefore, eventsBefore);
    }

    [Fact]
    public async Task A_second_request_while_the_guided_attempt_runs_is_refused_and_leaves_the_snapshot_and_manifest_untouched()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var store = new TestArtifactStore();
        await using var firstContext = _fixture.CreateContext();
        var first = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(
            (await StopHandler(firstContext, new RecordingEvidenceReader(seed.Evidence), store)
                .HandleAsync(GuidedCommand(seed, DirectCorrectionGuidance), CancellationToken.None)).Value);
        var manifestBefore = await ReadManifestAsync(store, seed.Run.Id, first.AttemptId);
        await using var secondContext = _fixture.CreateContext();

        var second = await StopHandler(secondContext, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore())
            .HandleAsync(GuidedCommand(seed, "SENTINEL-LATER another text"), CancellationToken.None);

        AssertCode(second, "attempts.run_has_active_attempt");
        Assert.Equal(DirectCorrectionGuidance, await ReadDirectGuidanceAsync(first.AttemptId));
        Assert.Equal(manifestBefore, await ReadManifestAsync(store, seed.Run.Id, first.AttemptId));
    }
}
