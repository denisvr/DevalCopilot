using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The one manual Resolver format-repair claim: it reuses the ordinary challenge-resolution claim (same
/// handler, same protections) and adds a source link, exact-input derivation and comparison, source-eligibility
/// checks at the request and again inside the short write-locked claim transaction, and a fixed manifest
/// reminder. Every refusal below creates no Attempt and leaves no orphaned sealed manifest.
/// </summary>
public sealed class CreateChallengeResolutionRepairAttemptTests : IAsyncLifetime
{
    private const string NotFound = "agent_attempts.repair_source_not_found";
    private const string Ineligible = "agent_attempts.repair_source_ineligible";
    private const string RepairOfRepair = "agent_attempts.repair_of_repair_forbidden";
    private const string AlreadyRequested = "agent_attempts.repair_already_requested";
    private const string NotLatest = "agent_attempts.repair_source_not_latest";
    private const string CheckpointMismatch = "agent_attempts.repair_source_checkpoint_mismatch";
    private const string InputsMismatch = "agent_attempts.repair_source_inputs_mismatch";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private CreateChallengeResolutionAttemptCommandHandler NewHandler(
        IDevalCopilotDbContext db, RepairEvidenceReader reader, RepairArtifactStore store, IAttemptDurabilityProbe? probe = null) =>
        new(db, reader, store, new FixedTimeProvider(RepairTestScene.Now), probe ?? new AttemptDurabilityProbe(_fixture.Options));

    private static string Code<T>(Result<T> result) => Assert.Single(result.Errors).Code;

    private static Task<Result<CreateChallengeResolutionAttemptCommandResult>> RepairAsync(
        CreateChallengeResolutionAttemptCommandHandler handler, Guid runId, Guid sourceId) =>
        handler.HandleAsync(CreateChallengeResolutionAttemptCommand.ForRepair(runId, sourceId), CancellationToken.None);

    private async Task AssertRefusedBeforeAnyExternalWorkAsync(
        Result<CreateChallengeResolutionAttemptCommandResult> result,
        string expectedCode,
        RepairTestScene scene,
        int attemptsBefore,
        RepairEvidenceReader reader,
        RepairArtifactStore store)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, Code(result));
        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, store.SealCount);
        Assert.Empty(store.DeletedSealedFiles);
        Assert.Equal(attemptsBefore, await scene.AttemptCountAsync());
    }

    [Fact]
    public async Task Repair_of_a_first_round_resolution_claims_a_linked_read_only_attempt_with_the_sources_exact_ordered_inputs()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, review, proposal) = scene.AddResolverSource(challengeCount: 3);
        await scene.SaveAsync();
        scene.Run.SetRequestedCodexAssignment("gpt-5-legacy", "low");
        await scene.SaveAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(source.AttemptNumber + 1, result.Value.AttemptNumber);
        Assert.Equal(source.Id, result.Value.RepairSourceAttemptId);

        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal(source.Id, repair.AgentRepairSourceAttemptId);
        Assert.Equal(AttemptStatus.Running, repair.Status);
        Assert.Equal(AgentProvider.Codex, repair.AgentProvider);
        Assert.Equal(AgentRole.Resolver, repair.AgentRole);
        Assert.Equal(AgentResponseContract.ChallengeResolution, repair.AgentResponseContract);
        Assert.Equal(AgentPermissionProfile.ReadOnly, repair.AgentPermissionProfile);
        Assert.Equal("codex-challenge-resolution-v1", repair.AgentAdapterContractVersion);
        Assert.Equal("gpt-5-legacy", repair.AgentRequestedModel);
        Assert.Equal("low", repair.AgentRequestedEffort);
        Assert.Equal(scene.Workspace.Id, repair.AgentGitWorkspaceId);
        Assert.Equal(scene.Checkpoint.Id, repair.AgentGitCheckpointId);

        var expectedInputs = new[] { proposal.Id }.Concat(review.Outputs.Select(challenge => challenge.Id)).ToList();
        var inputs = verify.AttemptInputMessages.Where(m => m.AttemptId == repair.Id).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(Enumerable.Range(0, 4), inputs.Select(input => input.Sequence));
        Assert.Equal(expectedInputs, inputs.Select(input => input.CollaborationMessageId));
        Assert.Single(verify.Artifacts.Where(a => a.AttemptId == repair.Id && a.Purpose == ArtifactPurpose.AgentContextManifest));
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == repair.Id || m.AttemptId == source.Id));
        Assert.Equal(1, reader.Calls);
        Assert.Empty(store.DeletedSealedFiles);
    }

    [Fact]
    public async Task Repair_of_a_second_round_resolution_retains_the_revised_proposal_and_its_second_round_challenges()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, review, revised) = scene.AddResolverSource(secondRound: true, challengeCount: 2);
        await scene.SaveAsync();
        var store = new RepairArtifactStore();

        var result = await RepairAsync(
            NewHandler(scene.Db, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await using var verify = _fixture.CreateContext();
        var inputs = verify.AttemptInputMessages.Where(m => m.AttemptId == result.Value.AttemptId).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(
            new[] { revised.Id }.Concat(review.Outputs.Select(challenge => challenge.Id)),
            inputs.Select(input => input.CollaborationMessageId));
        using var manifest = JsonDocument.Parse(store.ReadManifest(scene.Run.Id, result.Value.AttemptId));
        Assert.Equal(revised.Id, manifest.RootElement.GetProperty("originalProposal").GetProperty("messageId").GetGuid());
        Assert.Equal(2, manifest.RootElement.GetProperty("challenges").GetArrayLength());
    }

    [Fact]
    public async Task Repair_manifest_is_the_ordinary_manifest_plus_one_fixed_notice_inside_the_trusted_part()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        var store = new RepairArtifactStore();

        var result = await RepairAsync(
            NewHandler(scene.Db, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id);
        Assert.True(result.IsSuccess);
        var manifestText = store.ReadManifest(scene.Run.Id, result.Value.AttemptId);
        using var manifest = JsonDocument.Parse(manifestText);

        var members = manifest.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        var noticeIndex = members.IndexOf("formatRepairNotice");
        Assert.True(noticeIndex >= 0);
        Assert.Equal("expectedOutputSchema", members[noticeIndex - 1]);
        Assert.Equal("untrustedEvidenceBoundary", members[noticeIndex + 1]);
        Assert.Equal(ChallengeResolutionContextManifestBuilder.FormatRepairNotice,
            manifest.RootElement.GetProperty("formatRepairNotice").GetString());
        Assert.Equal(
            new[]
            {
                "protocolVersion", "expectedResponseContract", "objective", "projectId", "gitWorkspaceId", "gitCheckpointId",
                "checkpointFingerprintSha256", "instructionReferences", "instruction", "expectedOutputSchema",
                "untrustedEvidenceBoundary", "originalProposal", "challenges", "changeEvidence",
            },
            members.Where(name => name != "formatRepairNotice"));
        Assert.DoesNotContain(source.Id.ToString(), manifestText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidStructuredOutput", manifestText);
        Assert.DoesNotContain("parser", manifestText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_ordinary_request_is_unchanged_by_the_repair_capability()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (_, review, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        var store = new RepairArtifactStore();
        // The ordinary request targets the challenged review itself (not the failed resolution).
        await using var context = _fixture.CreateContext();
        var handler = NewHandler(context, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store);
        Assert.Equal(AgentOutcome.Challenged, review.Attempt.AgentOutcome);

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(scene.Run.Id, review.Attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Null(result.Value.RepairSourceAttemptId);
        Assert.Null((await context.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId)).AgentRepairSourceAttemptId);
        Assert.DoesNotContain("formatRepairNotice", store.ReadManifest(scene.Run.Id, result.Value.AttemptId));
    }

    [Fact]
    public async Task An_unknown_source_and_a_source_of_another_run_are_the_same_fixed_not_found()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        scene.AddResolverSource();
        var other = await RepairTestScene.CreateAsync(_fixture);
        var (foreignSource, _, _) = other.AddResolverSource();
        await scene.SaveAsync();
        await other.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();

        foreach (var sourceId in new[] { Guid.NewGuid(), foreignSource.Id })
        {
            var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
            var store = new RepairArtifactStore();
            var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, sourceId);

            await AssertRefusedBeforeAnyExternalWorkAsync(result, NotFound, scene, attemptsBefore, reader, store);
            Assert.Equal("The selected attempt was not found for this run.", Assert.Single(result.Errors).Description);
        }
    }

    public static TheoryData<string, string> IneligibleSourceCorruptions => new()
    {
        { "wrong permission profile", "AgentPermissionProfile = 'WorkspaceEditOnly'" },
        { "unknown adapter contract version", "AgentAdapterContractVersion = 'codex-challenge-resolution-v0'" },
        { "no adapter contract version", "AgentAdapterContractVersion = NULL" },
        { "absent process evidence", "AgentProcessOutcome = NULL, AgentProcessExitCode = NULL, AgentProcessDuration = NULL" },
        { "non-clean exit code", "AgentProcessExitCode = 1" },
        { "unexpected message type", "AgentExpectedMessageType = 'ReviewFinding'" },
        { "wrong provider", "AgentProvider = 'ClaudeCode'" },
        { "not dispatched", "AgentDispatchedAtUtc = NULL" },
        { "not concluded", "CompletedAtUtc = NULL" },
        { "wrong outcome", "AgentOutcome = 'ProviderInvocationFailed'" },
        { "success outcome", "AgentOutcome = 'Resolved', Status = 'Completed'" },
        { "still running", "Status = 'Running'" },
        { "unreadable outcome", "AgentOutcome = 'NoSuchOutcome'" },
        { "unreadable role", "AgentRole = 'NoSuchRole'" },
        { "unreadable provider", "AgentProvider = 'NoSuchProvider'" },
        { "malformed requested effort", "AgentRequestedEffort = '   '" },
    };

    [Theory]
    [MemberData(nameof(IneligibleSourceCorruptions))]
    public async Task A_source_that_is_not_the_exact_failed_resolution_shape_is_refused_before_any_external_work(
        string description, string assignments)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        await scene.CorruptAsync(source.Id, assignments);
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, scene, attemptsBefore, reader, store);
        Assert.Equal("The selected attempt cannot be repaired.", Assert.Single(result.Errors).Description);
        Assert.False(string.IsNullOrEmpty(description));
    }

    [Fact]
    public async Task A_source_that_recorded_a_decision_or_proposal_message_is_refused()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, review, proposal) = scene.AddResolverSource();
        scene.Db.CollaborationMessages.Add(CollaborationMessage.Record(
            Guid.NewGuid(), scene.Run.Id, source.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Decision,
            review.Outputs[0].Id, "Decision", JsonSerializer.Serialize(new
            {
                resolution = "accepted",
                rationale = "Why",
                resultingPlanChanges = "None",
                nextAction = "None",
            }), CollaborationMessageProvenance.ProviderObserved, RepairTestScene.Now));
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, scene, attemptsBefore, reader, store);
        Assert.NotEqual(Guid.Empty, proposal.Id);
    }

    [Fact]
    public async Task A_source_of_a_different_role_is_refused_at_the_resolution_operation()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (criticalSource, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, criticalSource.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task A_source_that_is_not_the_latest_agent_attempt_is_refused()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        scene.Lineage.AddRoot();
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, NotLatest, scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task The_source_is_repaired_at_most_once_and_a_repair_cannot_itself_be_repaired()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        var first = await RepairAsync(
            NewHandler(scene.Db, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), new RepairArtifactStore()),
            scene.Run.Id, source.Id);
        Assert.True(first.IsSuccess);
        var attemptsBefore = await scene.AttemptCountAsync();

        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();
        var second = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);
        await AssertRefusedBeforeAnyExternalWorkAsync(second, AlreadyRequested, scene, attemptsBefore, reader, store);

        await using (var completeContext = _fixture.CreateContext())
        {
            var repair = await completeContext.Attempts.SingleAsync(a => a.Id == first.Value.AttemptId);
            repair.MarkAgentDispatched(RepairTestScene.Now);
            repair.CompleteAgent(
                AgentOutcome.InvalidStructuredOutput, RepairTestScene.Fingerprint, RepairTestScene.Now,
                processEvidence: TestProcessEvidence.CleanExit);
            await completeContext.SaveChangesAsync();
        }

        var chainReader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var chainStore = new RepairArtifactStore();
        var chained = await RepairAsync(NewHandler(scene.Db, chainReader, chainStore), scene.Run.Id, first.Value.AttemptId);
        await AssertRefusedBeforeAnyExternalWorkAsync(chained, RepairOfRepair, scene, attemptsBefore, chainReader, chainStore);
    }

    [Fact]
    public async Task A_newer_workspace_checkpoint_refuses_the_repair_because_it_is_a_different_context()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        scene.Db.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), scene.Workspace.Id, 2, RepairTestScene.Now, new string('d', 40), new string('d', 64), []));
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(new string('d', 64));
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, CheckpointMismatch, scene, attemptsBefore, reader, store);
    }

    // ---- exact ordered input identity: a partial, reordered, extended, gapped, foreign, or replaced set
    // ---- is never substituted for the source's recorded set.

    private async Task AssertInputRefusalAsync(string description, string sql, string expectedCode, bool withSecondReview = false)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (_, proposal) = scene.Lineage.AddRoot();
        var review = scene.Lineage.AddReview(proposal, AgentOutcome.Challenged, challengeCount: 2);
        var decoyReview = withSecondReview ? scene.Lineage.AddReview(proposal, AgentOutcome.Challenged, challengeCount: 2) : null;
        var source = scene.AddInvalidResolver(proposal, review.Outputs);
        await scene.SaveAsync();
        await using (var context = _fixture.CreateContext())
        {
#pragma warning disable EF1002 // Fixed, test-owned SQL text with typed placeholders; no external input.
            await context.Database.ExecuteSqlRawAsync(
                sql, source.Id, review.Outputs[0].Id, review.Outputs[1].Id, decoyReview?.Outputs[0].Id ?? Guid.Empty, Guid.NewGuid());
#pragma warning restore EF1002
        }
        scene.Detach();

        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, expectedCode, scene, attemptsBefore, reader, store);
        Assert.False(string.IsNullOrEmpty(description));
    }

    [Fact]
    public async Task A_partial_challenge_set_is_refused_as_a_mismatch()
    {
        await AssertInputRefusalAsync(
            "last challenge dropped",
            "DELETE FROM attempt_input_messages WHERE AttemptId = {0} AND Sequence = 2",
            InputsMismatch);
    }

    [Fact]
    public async Task A_reordered_challenge_set_is_refused_as_a_mismatch()
    {
        await AssertInputRefusalAsync(
            "challenges swapped",
            "UPDATE attempt_input_messages SET Sequence = 9 WHERE AttemptId = {0} AND CollaborationMessageId = {1}; " +
            "UPDATE attempt_input_messages SET Sequence = 1 WHERE AttemptId = {0} AND CollaborationMessageId = {2}; " +
            "UPDATE attempt_input_messages SET Sequence = 2 WHERE AttemptId = {0} AND CollaborationMessageId = {1}",
            InputsMismatch);
    }

    [Fact]
    public async Task A_gapped_input_sequence_is_refused_as_ineligible()
    {
        await AssertInputRefusalAsync(
            "sequence gap",
            "UPDATE attempt_input_messages SET Sequence = 5 WHERE AttemptId = {0} AND Sequence = 2",
            Ineligible);
    }

    [Fact]
    public async Task A_challenge_set_extended_with_a_foreign_challenge_is_refused_as_a_mismatch()
    {
        await AssertInputRefusalAsync(
            "foreign challenge appended",
            "INSERT INTO attempt_input_messages (Id, AttemptId, CollaborationMessageId, Sequence) " +
            "VALUES ({4}, {0}, {3}, 3)",
            InputsMismatch,
            withSecondReview: true);
    }

    [Fact]
    public async Task A_source_with_only_the_proposal_and_no_challenge_is_refused_as_ineligible()
    {
        await AssertInputRefusalAsync(
            "no challenges",
            "DELETE FROM attempt_input_messages WHERE AttemptId = {0} AND Sequence > 0",
            Ineligible);
    }

    [Fact]
    public async Task A_challenge_that_no_longer_replies_to_the_original_proposal_is_refused_by_the_ordinary_validation()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, review, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE collaboration_messages SET InReplyToMessageId = {review.Outputs[0].Id} WHERE Id = {review.Outputs[1].Id}");
        }
        scene.Detach();

        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, "agent_attempts.challenges_not_valid", scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task An_unreadable_challenge_message_row_refuses_the_repair_without_a_crash()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, review, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE collaboration_messages SET Provenance = 'NoSuchProvenance' WHERE Id = {review.Outputs[0].Id}");
        }
        scene.Detach();

        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, store.SealCount);
        Assert.Equal(attemptsBefore, await scene.AttemptCountAsync());
    }

    [Fact]
    public async Task A_challenged_review_that_was_already_resolved_successfully_refuses_the_repair()
    {
        // The review's challenges already have a successful resolution by a newer attempt, so the source is
        // no longer the latest attempt and an ordinary request would be refused as already resolved.
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (_, proposal) = scene.Lineage.AddRoot();
        var review = scene.Lineage.AddReview(proposal, AgentOutcome.Challenged, challengeCount: 2);
        var source = scene.AddInvalidResolver(proposal, review.Outputs);
        scene.Lineage.AddResolution(proposal, review.Outputs);
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await RepairAsync(NewHandler(scene.Db, reader, store), scene.Run.Id, source.Id);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, NotLatest, scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task The_ordinary_claim_gates_still_refuse_a_repair_without_any_external_work()
    {
        var exhausted = await RepairTestScene.CreateAsync(_fixture, maximumAgentAttempts: 3);
        var (exhaustedSource, _, _) = exhausted.AddResolverSource();
        await exhausted.SaveAsync();
        await AssertGateAsync(exhausted, exhaustedSource, "agent_attempts.budget_exhausted");

        var timed = await RepairTestScene.CreateAsync(_fixture, maximumAgentInvocationTime: TimeSpan.FromMinutes(1));
        var (timedSource, _, _) = timed.AddResolverSource();
        await timed.SaveAsync();
        await AssertGateAsync(timed, timedSource, "agent_attempts.time_budget_exceeded");

        var stopped = await RepairTestScene.CreateAsync(_fixture);
        stopped.SourceTokenUsage = TokenStopTestSupport.CodexUsage(500, 50);
        var (stoppedSource, _, _) = stopped.AddResolverSource();
        await stopped.SaveAsync();
        await TokenStopTestSupport.SetStopAsync(_fixture, stopped.Run.Id, AgentProvider.Codex, 550);
        await AssertGateAsync(stopped, stoppedSource, AgentTokenStopGate.ReachedCode);

        async Task AssertGateAsync(RepairTestScene gated, Attempt source, string code)
        {
            var attemptsBefore = await gated.AttemptCountAsync();
            var gateReader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
            var gateStore = new RepairArtifactStore();
            await using var freshContext = _fixture.CreateContext();
            var result = await RepairAsync(NewHandler(freshContext, gateReader, gateStore), gated.Run.Id, source.Id);
            await AssertRefusedBeforeAnyExternalWorkAsync(result, code, gated, attemptsBefore, gateReader, gateStore);
        }
    }

    // ---- the actual commit seam

    private sealed record Seam(
        RepairTestScene Scene,
        Attempt Source,
        PlanningLineageSeeder.Review Review,
        PlanningLineageSeeder.Review? DecoyReview,
        CollaborationMessage Proposal,
        DevalCopilotDbContext Racing);

    private async Task AssertSeamRefusalAsync(
        string description, Func<Seam, Task> seamChange, string expectedCode, bool withDecoyReview = false)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (_, proposal) = scene.Lineage.AddRoot();
        var review = scene.Lineage.AddReview(proposal, AgentOutcome.Challenged, challengeCount: 2);
        var decoy = withDecoyReview ? scene.Lineage.AddReview(proposal, AgentOutcome.Challenged, challengeCount: 2) : null;
        var source = scene.AddInvalidResolver(proposal, review.Outputs);
        await scene.SaveAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var racing = _fixture.CreateContext();
                await seamChange(new Seam(scene, source, review, decoy, proposal, racing));
                await racing.SaveChangesAsync(CancellationToken.None);
            },
        };

        var result = await RepairAsync(NewHandler(faulting, reader, store), scene.Run.Id, source.Id);

        Assert.True(result.IsFailure, description);
        Assert.Equal(expectedCode, Code(result));
        Assert.Equal(1, reader.Calls);
        Assert.Equal(1, store.SealCount);
        var deleted = Assert.Single(store.DeletedSealedFiles);
        Assert.Equal(scene.Run.Id, deleted.RunId);
        Assert.Equal(ArtifactPurpose.AgentContextManifest, deleted.Purpose);
        Assert.Equal(store.SealedAttemptIds[0], deleted.AttemptId);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == deleted.AttemptId));
        Assert.Empty(verify.AttemptInputMessages.Where(m => m.AttemptId == deleted.AttemptId));
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == deleted.AttemptId));
    }

    [Fact]
    public async Task A_competing_repair_committed_at_the_claim_seam_is_refused_and_rolled_back_completely()
    {
        await AssertSeamRefusalAsync(
            "competing repair",
            seam =>
            {
                var number = seam.Scene.Lineage.ReserveAttemptNumber();
                var competing = Attempt.ClaimAgentChallengeResolutionWithAssignment(
                    Guid.NewGuid(), seam.Scene.Run.Id, number, seam.Scene.Workspace.Id, seam.Scene.Checkpoint.Id,
                    RepairTestScene.Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now,
                    null, null, number, seam.Source.Id);
                seam.Racing.Attempts.Add(competing);
                seam.Racing.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, seam.Proposal.Id, sequence: 0));
                seam.Racing.AttemptInputMessages.Add(
                    AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, seam.Review.Outputs[0].Id, sequence: 1));
                return Task.CompletedTask;
            },
            AlreadyRequested);
    }

    [Fact]
    public async Task A_newer_agent_attempt_committed_at_the_claim_seam_makes_the_source_not_latest()
    {
        await AssertSeamRefusalAsync(
            "newer attempt",
            seam =>
            {
                var number = seam.Scene.Lineage.ReserveAttemptNumber();
                var planner = Attempt.ClaimAgent(
                    Guid.NewGuid(), seam.Scene.Run.Id, number, seam.Scene.Workspace.Id, seam.Scene.Checkpoint.Id,
                    RepairTestScene.Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now, number);
                planner.Fail(RepairTestScene.Now);
                seam.Racing.Attempts.Add(planner);
                return Task.CompletedTask;
            },
            NotLatest);
    }

    [Fact]
    public async Task A_challenge_input_removed_at_the_claim_seam_is_refused_as_a_mismatch()
    {
        await AssertSeamRefusalAsync(
            "challenge dropped",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM attempt_input_messages WHERE AttemptId = {seam.Source.Id} AND Sequence = 2"),
            InputsMismatch);
    }

    [Fact]
    public async Task Challenge_inputs_reordered_at_the_claim_seam_are_refused_as_a_mismatch()
    {
        await AssertSeamRefusalAsync(
            "challenges swapped",
            async seam =>
            {
                var first = seam.Review.Outputs[0].Id;
                var second = seam.Review.Outputs[1].Id;
                await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempt_input_messages SET Sequence = 9 WHERE AttemptId = {seam.Source.Id} AND CollaborationMessageId = {first}");
                await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempt_input_messages SET Sequence = 1 WHERE AttemptId = {seam.Source.Id} AND CollaborationMessageId = {second}");
                await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempt_input_messages SET Sequence = 2 WHERE AttemptId = {seam.Source.Id} AND CollaborationMessageId = {first}");
            },
            InputsMismatch);
    }

    [Fact]
    public async Task Inputs_replaced_by_another_reviews_challenges_at_the_claim_seam_are_refused_as_a_mismatch()
    {
        await AssertSeamRefusalAsync(
            "another review substituted",
            async seam =>
            {
                var other = seam.DecoyReview!.Outputs;
                await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempt_input_messages SET CollaborationMessageId = {other[0].Id} WHERE AttemptId = {seam.Source.Id} AND Sequence = 1");
                await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempt_input_messages SET CollaborationMessageId = {other[1].Id} WHERE AttemptId = {seam.Source.Id} AND Sequence = 2");
            },
            InputsMismatch,
            withDecoyReview: true);
    }

    // The claim's context stays alive and populated (the scene tracked every seeded row): only untracked reads at the
    // seam can see a row an independent connection changed immediately before BEGIN.

    [Fact]
    public async Task The_owning_challenged_review_failing_at_the_claim_seam_is_refused_despite_the_tracked_row()
    {
        await AssertSeamRefusalAsync(
            "review failed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET Status = 'Failed', AgentOutcome = 'InvalidStructuredOutput' WHERE Id = {seam.Review.Attempt.Id}"),
            "agent_attempts.challenged_review_not_valid");
    }

    [Fact]
    public async Task A_challenge_actor_provider_changed_at_the_claim_seam_is_refused_despite_the_tracked_row()
    {
        await AssertSeamRefusalAsync(
            "challenge actor changed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE collaboration_messages SET ActorAgentProvider = 'Codex' WHERE Id = {seam.Review.Outputs[0].Id}"),
            "agent_attempts.challenges_not_valid");
    }

    [Fact]
    public async Task A_challenge_reply_target_changed_at_the_claim_seam_is_refused_despite_the_tracked_row()
    {
        await AssertSeamRefusalAsync(
            "challenge reply changed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE collaboration_messages SET InReplyToMessageId = {seam.Review.Outputs[0].Id} WHERE Id = {seam.Review.Outputs[1].Id}"),
            "agent_attempts.challenges_not_valid");
    }

    [Fact]
    public async Task A_semantic_message_recorded_by_the_source_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "message recorded",
            seam =>
            {
                seam.Racing.CollaborationMessages.Add(CollaborationMessage.Record(
                    Guid.NewGuid(), seam.Scene.Run.Id, seam.Source.Id, CollaborationMessage.ProtocolVersionOne,
                    ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex),
                    ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Decision,
                    seam.Review.Outputs[0].Id, "Decision", JsonSerializer.Serialize(new
                    {
                        resolution = "accepted",
                        rationale = "Why",
                        resultingPlanChanges = "None",
                        nextAction = "None",
                    }), CollaborationMessageProvenance.ProviderObserved, RepairTestScene.Now));
                return Task.CompletedTask;
            },
            Ineligible);
    }

    [Fact]
    public async Task A_new_workspace_checkpoint_committed_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "new checkpoint",
            seam =>
            {
                seam.Racing.GitCheckpoints.Add(GitCheckpoint.Capture(
                    Guid.NewGuid(), seam.Scene.Workspace.Id, 2, RepairTestScene.Now, new string('d', 40), new string('d', 64), []));
                return Task.CompletedTask;
            },
            "agent_attempts.checkpoint_not_current");
    }

    [Fact]
    public async Task A_lease_released_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "lease released",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE repository_mutation_leases SET Status = 'Released' WHERE WorkspaceId = {seam.Scene.Workspace.Id}"),
            "agent_attempts.lease_not_active");
    }

    [Fact]
    public async Task A_run_that_ended_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "run ended",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE runs SET Lifecycle = 'Failed' WHERE Id = {seam.Scene.Run.Id}"),
            "runs.not_running");
    }

    [Fact]
    public async Task A_running_process_attempt_committed_at_the_claim_seam_is_refused_as_an_active_attempt()
    {
        await AssertSeamRefusalAsync(
            "process attempt",
            seam =>
            {
                seam.Racing.Attempts.Add(
                    Attempt.Claim(Guid.NewGuid(), seam.Scene.Run.Id, seam.Scene.Lineage.ReserveAttemptNumber(), RepairTestScene.Now));
                return Task.CompletedTask;
            },
            "attempts.run_has_active_attempt");
    }

    [Fact]
    public async Task A_model_preference_change_committed_at_the_claim_seam_is_refused_by_the_preference_guard()
    {
        await AssertSeamRefusalAsync(
            "preference changed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE runs SET RequestedCodexModel = 'gpt-6-sol', RequestedCodexEffort = 'high' WHERE Id = {seam.Scene.Run.Id}"),
            "agent_attempts.assignment_preference_changed");
    }

    [Fact]
    public async Task A_stop_threshold_change_committed_at_the_claim_seam_is_refused_by_the_policy_guard()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ => await TokenStopTestSupport.SetStopAsync(
                _fixture, scene.Run.Id, AgentProvider.Codex, 1_000_000),
        };

        var result = await RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrentTokenStopPolicy.PolicyChangedDuringClaimCode, Code(result));
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Concurrent_repair_claims_of_one_source_commit_exactly_one_repair()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        using var barrier = new Barrier(2);
        RepairEvidenceReader Reader() => new(UntrackedManifestTestSupport.Evidence(RepairTestScene.Fingerprint))
        {
            OnFirstCapture = _ => Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(30))),
        };

        var firstStore = new RepairArtifactStore();
        var secondStore = new RepairArtifactStore();
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var results = await Task.WhenAll(
            RepairAsync(NewHandler(firstContext, Reader(), firstStore), scene.Run.Id, source.Id),
            RepairAsync(NewHandler(secondContext, Reader(), secondStore), scene.Run.Id, source.Id));

        var winner = Assert.Single(results, r => r.IsSuccess);
        var loser = Assert.Single(results, r => r.IsFailure);
        Assert.Contains(Code(loser), new[] { AlreadyRequested, NotLatest, "attempts.run_has_active_attempt" });

        await using var verify = _fixture.CreateContext();
        var repair = Assert.Single(verify.Attempts.Where(a => a.AgentRepairSourceAttemptId == source.Id));
        Assert.Equal(winner.Value.AttemptId, repair.Id);
        var removed = Assert.Single(firstStore.DeletedSealedFiles.Concat(secondStore.DeletedSealedFiles));
        Assert.NotEqual(repair.Id, removed.AttemptId);
        Assert.Single(verify.Artifacts.Where(a => a.AttemptId == repair.Id));
        Assert.Equal(2, verify.Attempts.Count(a => a.RunId == scene.Run.Id && a.AgentRole == AgentRole.Resolver));
    }

    // ---- durability and cancellation at the claim boundary

    private async Task<(RepairTestScene Scene, Attempt Source)> SeedSimpleAsync()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        return (scene, source);
    }

    [Fact]
    public async Task A_transaction_that_cannot_be_acquired_fails_closed_and_removes_the_sealed_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { ThrowOnBeginTransaction = true };

        var result = await RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Code(result));
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Cancellation_while_acquiring_the_transaction_propagates_and_removes_the_sealed_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { ThrowCancellationOnBeginTransaction = true };

        await Assert.ThrowsAsync<OperationCanceledException>(() => RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id));

        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task A_commit_that_fails_without_persisting_is_a_safe_failure_and_removes_the_sealed_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit };

        var result = await RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Code(result));
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task A_commit_that_completes_before_the_failure_is_reported_is_resolved_as_success_and_keeps_the_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.AfterCommit };

        var result = await RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(source.Id, result.Value.RepairSourceAttemptId);
        Assert.Empty(store.DeletedSealedFiles);
        Assert.Contains(await scene.AttemptsAsync(), a => a.Id == result.Value.AttemptId && a.AgentRepairSourceAttemptId == source.Id);
    }

    [Fact]
    public async Task An_ambiguous_commit_the_probe_cannot_resolve_is_reported_unresolved_and_never_deletes_the_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit };
        var probe = new FixedDurabilityProbe(AttemptDurabilityCheckResult.Unresolved);

        var result = await RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store, probe), scene.Run.Id, source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_unresolved", Code(result));
        Assert.Empty(store.DeletedSealedFiles);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Cancellation_before_the_commit_propagates_and_removes_the_manifest_when_nothing_persisted()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.CancellationBeforeCommit };

        await Assert.ThrowsAsync<OperationCanceledException>(() => RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id));

        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Cancellation_after_the_commit_propagates_and_preserves_the_persisted_attempts_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.CancellationAfterCommit };

        await Assert.ThrowsAsync<OperationCanceledException>(() => RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id));

        Assert.Empty(store.DeletedSealedFiles);
        Assert.Contains(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId == source.Id);
    }

    [Fact]
    public async Task A_save_that_fails_without_persisting_is_a_safe_failure_and_removes_the_sealed_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave,
        };

        var result = await RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Code(result));
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Cancellation_after_the_save_was_applied_still_rolls_back_and_removes_the_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.CancellationAfterSave,
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => RepairAsync(
            NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store), scene.Run.Id, source.Id));

        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Deleting_a_repaired_source_alone_is_refused_while_deleting_the_run_removes_both()
    {
        var (scene, source) = await SeedSimpleAsync();
        var repair = await RepairAsync(
            NewHandler(scene.Db, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), new RepairArtifactStore()),
            scene.Run.Id, source.Id);
        Assert.True(repair.IsSuccess);

        await using (var context = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM attempts WHERE Id = {source.Id}"));
        }

        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM runs WHERE Id = {scene.Run.Id}");
            Assert.Empty(context.Attempts.Where(a => a.RunId == scene.Run.Id));
        }
    }
}
