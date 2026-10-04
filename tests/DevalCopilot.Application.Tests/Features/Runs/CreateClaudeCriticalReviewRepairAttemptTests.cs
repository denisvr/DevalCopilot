using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The one manual CriticalReviewer format-repair claim: it reuses the ordinary critical-review claim (same
/// handler, same protections) and adds a source link, source-eligibility checks at the request and again
/// inside a short write-locked transaction at the durable claim boundary, and a fixed manifest reminder.
/// Every refusal below creates no Attempt and leaves no orphaned sealed manifest.
/// </summary>
public sealed class CreateClaudeCriticalReviewRepairAttemptTests : IAsyncLifetime
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

    private CreateClaudeCriticalReviewAttemptCommandHandler NewHandler(
        IDevalCopilotDbContext db, RepairEvidenceReader reader, RepairArtifactStore store, IAttemptDurabilityProbe? probe = null) =>
        new(db, reader, store, new FixedTimeProvider(RepairTestScene.Now), probe ?? new AttemptDurabilityProbe(_fixture.Options));

    private static string Code<T>(Result<T> result) => Assert.Single(result.Errors).Code;

    private async Task AssertRefusedBeforeAnyExternalWorkAsync(
        Result<CreateClaudeCriticalReviewAttemptCommandResult> result,
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
    public async Task Repair_of_a_root_proposal_review_claims_a_linked_read_only_attempt_with_the_sources_exact_input()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, proposal) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(source.AttemptNumber + 1, result.Value.AttemptNumber);
        Assert.Equal(source.Id, result.Value.RepairSourceAttemptId);

        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal(source.Id, repair.AgentRepairSourceAttemptId);
        Assert.Equal(AttemptStatus.Running, repair.Status);
        Assert.Equal(AgentProvider.ClaudeCode, repair.AgentProvider);
        Assert.Equal(AgentRole.CriticalReviewer, repair.AgentRole);
        Assert.Equal(AgentResponseContract.CriticalReview, repair.AgentResponseContract);
        Assert.Equal(AgentPermissionProfile.ReadOnly, repair.AgentPermissionProfile);
        Assert.Equal("claude-critical-review-v1", repair.AgentAdapterContractVersion);
        Assert.Equal(scene.Workspace.Id, repair.AgentGitWorkspaceId);
        Assert.Equal(scene.Checkpoint.Id, repair.AgentGitCheckpointId);
        Assert.Equal(source.AgentBudgetSlot + 1, repair.AgentBudgetSlot);

        var input = Assert.Single(verify.AttemptInputMessages.Where(m => m.AttemptId == repair.Id));
        Assert.Equal(0, input.Sequence);
        Assert.Equal(proposal.Id, input.CollaborationMessageId);

        var manifestArtifact = await verify.Artifacts.SingleAsync(
            a => a.AttemptId == repair.Id && a.Purpose == ArtifactPurpose.AgentContextManifest);
        Assert.Equal(repair.AgentContextManifestArtifactId, manifestArtifact.Id);

        // The source is untouched, and a claim never records a collaboration fact of the repair.
        var persistedSource = await verify.Attempts.SingleAsync(a => a.Id == source.Id);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, persistedSource.AgentOutcome);
        Assert.Null(persistedSource.AgentRepairSourceAttemptId);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == repair.Id || m.AttemptId == source.Id));
        Assert.Equal(1, reader.Calls);
        Assert.Empty(store.DeletedSealedFiles);
    }

    [Fact]
    public async Task Repair_of_a_first_resolver_revision_review_retains_the_revised_proposal_and_its_bounded_lineage()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, revised) = scene.AddCriticalReviewSource(reviseFirst: true);
        await scene.SaveAsync();
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await using var verify = _fixture.CreateContext();
        var input = Assert.Single(verify.AttemptInputMessages.Where(m => m.AttemptId == result.Value.AttemptId));
        Assert.Equal(revised.Id, input.CollaborationMessageId);
        using var manifest = JsonDocument.Parse(store.ReadManifest(scene.Run.Id, result.Value.AttemptId));
        Assert.Equal(revised.Id, manifest.RootElement.GetProperty("reviewedProposal").GetProperty("messageId").GetGuid());
    }

    [Fact]
    public async Task Repair_manifest_is_the_ordinary_manifest_plus_one_fixed_notice_inside_the_trusted_part()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);
        Assert.True(result.IsSuccess);
        var manifestText = store.ReadManifest(scene.Run.Id, result.Value.AttemptId);
        using var repairManifest = JsonDocument.Parse(manifestText);

        var members = repairManifest.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        var noticeIndex = members.IndexOf("formatRepairNotice");
        Assert.True(noticeIndex >= 0);
        Assert.Equal("expectedOutputSchema", members[noticeIndex - 1]);
        Assert.Equal("untrustedEvidenceBoundary", members[noticeIndex + 1]);
        Assert.Equal(ClaudeCriticalReviewContextManifestBuilder.FormatRepairNotice,
            repairManifest.RootElement.GetProperty("formatRepairNotice").GetString());

        // The same document without the notice is exactly an ordinary manifest, member for member.
        var withoutNotice = members.Where(name => name != "formatRepairNotice").ToList();
        var ordinaryMembers = new[]
        {
            "protocolVersion", "expectedResponseContract", "objective", "projectId", "gitWorkspaceId", "gitCheckpointId",
            "checkpointFingerprintSha256", "reviewCriteria", "expectedOutputSchema",
            "untrustedEvidenceBoundary", "projectInstructionContextBoundary", "projectInstructionContext", "reviewedProposal", "changeEvidence",
        };
        Assert.Equal(ordinaryMembers, withoutNotice);

        // The unchanged schema and boundary are present; nothing about the source leaks into the manifest.
        Assert.Equal("CriticalReview", repairManifest.RootElement.GetProperty("expectedResponseContract").GetString());
        Assert.Contains("untrusted evidence", repairManifest.RootElement.GetProperty("untrustedEvidenceBoundary").GetString());
        Assert.DoesNotContain(source.Id.ToString(), manifestText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(source.AgentContextManifestArtifactId!.Value.ToString(), manifestText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidStructuredOutput", manifestText);
        Assert.DoesNotContain("parser", manifestText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_ordinary_request_is_unchanged_by_the_repair_capability()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (_, proposal) = scene.Lineage.AddRoot();
        await scene.SaveAsync();
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(scene.Run.Id, proposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.RepairSourceAttemptId);
        await using var verify = _fixture.CreateContext();
        Assert.Null((await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId)).AgentRepairSourceAttemptId);
        Assert.DoesNotContain("formatRepairNotice", store.ReadManifest(scene.Run.Id, result.Value.AttemptId));
    }

    [Fact]
    public async Task An_unknown_source_and_a_source_of_another_run_are_the_same_fixed_not_found()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        scene.AddCriticalReviewSource();
        var other = await RepairTestScene.CreateAsync(_fixture);
        var (foreignSource, _) = other.AddCriticalReviewSource();
        await scene.SaveAsync();
        await other.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();

        foreach (var sourceId in new[] { Guid.NewGuid(), foreignSource.Id })
        {
            var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
            var store = new RepairArtifactStore();
            var result = await NewHandler(scene.Db, reader, store)
                .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, sourceId), CancellationToken.None);

            await AssertRefusedBeforeAnyExternalWorkAsync(result, NotFound, scene, attemptsBefore, reader, store);
            Assert.Equal("The selected attempt was not found for this run.", Assert.Single(result.Errors).Description);
        }
    }

    public static TheoryData<string, string> IneligibleSourceCorruptions => new()
    {
        { "wrong permission profile", "AgentPermissionProfile = 'WorkspaceEditOnly'" },
        { "unknown adapter contract version", "AgentAdapterContractVersion = 'claude-critical-review-v0'" },
        { "no adapter contract version", "AgentAdapterContractVersion = NULL" },
        { "absent process evidence", "AgentProcessOutcome = NULL, AgentProcessExitCode = NULL, AgentProcessDuration = NULL" },
        { "non-clean exit code", "AgentProcessExitCode = 1" },
        { "unexpected message type", "AgentExpectedMessageType = 'ReviewFinding'" },
        { "wrong provider", "AgentProvider = 'Codex'" },
        { "not dispatched", "AgentDispatchedAtUtc = NULL" },
        { "not concluded", "CompletedAtUtc = NULL" },
        { "wrong outcome", "AgentOutcome = 'ProviderInvocationFailed'" },
        { "success outcome", "AgentOutcome = 'Accepted', Status = 'Completed'" },
        { "still running", "Status = 'Running'" },
        { "unreadable outcome", "AgentOutcome = 'NoSuchOutcome'" },
        { "unreadable role", "AgentRole = 'NoSuchRole'" },
        { "unreadable status", "Status = 'NoSuchStatus'" },
        { "unreadable contract", "AgentResponseContract = 'NoSuchContract'" },
        { "malformed requested model", "AgentRequestedModel = '   '" },
    };

    [Theory]
    [MemberData(nameof(IneligibleSourceCorruptions))]
    public async Task A_source_that_is_not_the_exact_failed_critical_review_shape_is_refused_before_any_external_work(
        string description, string assignments)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        await scene.CorruptAsync(source.Id, assignments);
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, scene, attemptsBefore, reader, store);
        // The refusal never echoes a stored value.
        var message = Assert.Single(result.Errors).Description;
        Assert.Equal("The selected attempt cannot be repaired.", message);
        Assert.False(string.IsNullOrEmpty(description));
    }

    [Fact]
    public async Task A_source_that_recorded_a_semantic_result_message_is_refused()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, proposal) = scene.AddCriticalReviewSource();
        scene.Lineage.AddReviewMessage(
            source, Guid.NewGuid(), CollaborationMessageType.Acceptance, proposal.Id, "Accepted.",
            JsonSerializer.Serialize(new { rationale = "Sound." }));
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task A_source_of_a_different_role_is_refused_at_the_critical_review_operation()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (resolverSource, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, resolverSource.Id), CancellationToken.None);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task A_source_that_is_not_the_latest_agent_attempt_is_refused()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        scene.Lineage.AddRoot();
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, NotLatest, scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task The_source_is_repaired_at_most_once_and_a_repair_cannot_itself_be_repaired()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var store = new RepairArtifactStore();
        var first = await NewHandler(scene.Db, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);
        Assert.True(first.IsSuccess);
        var attemptsBefore = await scene.AttemptCountAsync();

        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var secondStore = new RepairArtifactStore();
        var second = await NewHandler(scene.Db, reader, secondStore)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);
        await AssertRefusedBeforeAnyExternalWorkAsync(second, AlreadyRequested, scene, attemptsBefore, reader, secondStore);

        // The repair itself fails structurally too: it is not a source (no repair chain).
        var repairId = first.Value.AttemptId;
        await using (var completeContext = _fixture.CreateContext())
        {
            var repair = await completeContext.Attempts.SingleAsync(a => a.Id == repairId);
            repair.MarkAgentDispatched(RepairTestScene.Now);
            repair.CompleteAgent(
                AgentOutcome.InvalidStructuredOutput, RepairTestScene.Fingerprint, RepairTestScene.Now,
                processEvidence: TestProcessEvidence.CleanExit);
            await completeContext.SaveChangesAsync();
        }

        var chainReader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var chainStore = new RepairArtifactStore();
        var chained = await NewHandler(scene.Db, chainReader, chainStore)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, repairId), CancellationToken.None);
        await AssertRefusedBeforeAnyExternalWorkAsync(chained, RepairOfRepair, scene, attemptsBefore, chainReader, chainStore);
    }

    [Fact]
    public async Task A_newer_workspace_checkpoint_refuses_the_repair_because_it_is_a_different_context()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        scene.Db.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), scene.Workspace.Id, 2, RepairTestScene.Now, new string('d', 40), new string('d', 64), []));
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(new string('d', 64));
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, CheckpointMismatch, scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task A_workspace_that_no_longer_matches_git_evidence_is_refused_after_capture_and_removes_nothing_it_sealed()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var reader = RepairEvidenceReader.Matching(new string('e', 64));
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", Code(result));
        Assert.Equal(1, reader.Calls);
        Assert.Equal(0, store.SealCount);
    }

    [Theory]
    [InlineData("no inputs", "DELETE FROM attempt_input_messages WHERE AttemptId = {0}")]
    public async Task A_source_whose_recorded_input_is_missing_is_refused(string description, string sql)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        await using (var context = _fixture.CreateContext())
        {
#pragma warning disable EF1002 // Fixed, test-owned SQL text; no external input.
            await context.Database.ExecuteSqlRawAsync(sql, source.Id);
#pragma warning restore EF1002
        }
        scene.Detach();

        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, scene, attemptsBefore, reader, store);
        Assert.False(string.IsNullOrEmpty(description));
    }

    [Fact]
    public async Task A_source_with_more_than_one_recorded_input_is_refused_as_a_partial_or_duplicate_identity()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (_, otherProposal) = scene.Lineage.AddRoot();
        var (source, proposal) = scene.AddCriticalReviewSource();
        scene.Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), source.Id, otherProposal.Id, sequence: 1));
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, Ineligible, scene, attemptsBefore, reader, store);
        Assert.NotEqual(proposal.Id, otherProposal.Id);
    }

    [Fact]
    public async Task A_lineage_that_no_longer_allows_a_review_refuses_the_repair_before_any_external_work()
    {
        // The reviewed Proposal was already successfully reviewed by a newer attempt: that attempt is the
        // latest, so the source is refused as not-latest, and an ordinary request is what the owner needs.
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, proposal) = scene.AddCriticalReviewSource();
        scene.Lineage.AddReview(proposal, AgentOutcome.Accepted);
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, NotLatest, scene, attemptsBefore, reader, store);
    }

    [Fact]
    public async Task A_corrupt_reviewed_proposal_lineage_refuses_the_repair_without_a_crash()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, proposal) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE collaboration_messages SET Type = 'NoSuchType' WHERE Id = {proposal.Id}");
        }
        scene.Detach();

        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, store.SealCount);
        Assert.Empty(store.DeletedSealedFiles);
        Assert.Equal(source.AttemptNumber, await scene.AttemptCountAsync());
    }

    [Fact]
    public async Task The_ordinary_claim_gates_still_refuse_a_repair_without_any_external_work()
    {
        // Count budget: the run's whole budget is already consumed by the source's history.
        var exhausted = await RepairTestScene.CreateAsync(_fixture, maximumAgentAttempts: 2);
        var (exhaustedSource, _) = exhausted.AddCriticalReviewSource();
        await exhausted.SaveAsync();
        await AssertGateAsync(exhausted, exhaustedSource, "agent_attempts.budget_exhausted");

        // Time budget: one more 10-minute reservation cannot fit the ceiling.
        var timed = await RepairTestScene.CreateAsync(_fixture, maximumAgentInvocationTime: TimeSpan.FromMinutes(1));
        var (timedSource, _) = timed.AddCriticalReviewSource();
        await timed.SaveAsync();
        await AssertGateAsync(timed, timedSource, "agent_attempts.time_budget_exceeded");

        // Provider token stop: recorded Claude usage has already reached the run's stop.
        var stopped = await RepairTestScene.CreateAsync(_fixture);
        stopped.SourceTokenUsage = TokenStopTestSupport.ClaudeUsage(500, 50, 5, 60);
        var (stoppedSource, _) = stopped.AddCriticalReviewSource();
        await stopped.SaveAsync();
        await TokenStopTestSupport.SetStopAsync(_fixture, stopped.Run.Id, AgentProvider.ClaudeCode, 615);
        await AssertGateAsync(stopped, stoppedSource, AgentTokenStopGate.ReachedCode);

        // A terminal run cannot start any attempt.
        var ended = await RepairTestScene.CreateAsync(_fixture);
        var (endedSource, _) = ended.AddCriticalReviewSource();
        await ended.SaveAsync();
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET Lifecycle = 'Failed' WHERE Id = {ended.Run.Id}");
        }

        await using var refreshed = _fixture.CreateContext();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();
        var terminal = await NewHandler(refreshed, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(ended.Run.Id, endedSource.Id), CancellationToken.None);
        Assert.True(terminal.IsFailure);
        Assert.Equal("runs.not_active", Code(terminal));
        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, store.SealCount);

        async Task AssertGateAsync(RepairTestScene gated, Attempt source, string code)
        {
            var attemptsBefore = await gated.AttemptCountAsync();
            var gateReader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
            var gateStore = new RepairArtifactStore();
            await using var freshContext = _fixture.CreateContext();
            var result = await NewHandler(freshContext, gateReader, gateStore)
                .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(gated.Run.Id, source.Id), CancellationToken.None);
            await AssertRefusedBeforeAnyExternalWorkAsync(result, code, gated, attemptsBefore, gateReader, gateStore);
        }
    }

    [Fact]
    public async Task A_running_process_attempt_still_blocks_a_repair_with_the_ordinary_active_attempt_conflict()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        scene.Db.Attempts.Add(Attempt.Claim(Guid.NewGuid(), scene.Run.Id, scene.Lineage.ReserveAttemptNumber(), RepairTestScene.Now));
        await scene.SaveAsync();
        var attemptsBefore = await scene.AttemptCountAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await NewHandler(scene.Db, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        await AssertRefusedBeforeAnyExternalWorkAsync(result, "attempts.run_has_active_attempt", scene, attemptsBefore, reader, store);
    }

    // ---- the actual commit seam: every change lands after the handler's early reads, sealed manifest, and
    // ---- transaction acquisition, so only the in-transaction re-reads (never a late read before it) can see it.

    private sealed record Seam(
        RepairTestScene Scene, Attempt Source, CollaborationMessage Proposal, CollaborationMessage? Decoy, DevalCopilotDbContext Racing);

    private async Task AssertSeamRefusalAsync(
        string description, Func<Seam, Task> seamChange, string expectedCode, bool withDecoyRoot = false)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var decoy = withDecoyRoot ? scene.Lineage.AddRoot().Proposal : null;
        var (source, proposal) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var racing = _fixture.CreateContext();
                await seamChange(new Seam(scene, source, proposal, decoy, racing));
                await racing.SaveChangesAsync(CancellationToken.None);
            },
        };

        var result = await NewHandler(faulting, reader, store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure, description);
        Assert.Equal(expectedCode, Code(result));

        // Complete rollback: only the seam's own change exists; the repair's sealed manifest was removed.
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
                var competing = Attempt.ClaimAgentCriticalReviewWithModelRequest(
                    Guid.NewGuid(), seam.Scene.Run.Id, number, seam.Scene.Workspace.Id, seam.Scene.Checkpoint.Id,
                    RepairTestScene.Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now,
                    null, null, number, seam.Source.Id);
                seam.Racing.Attempts.Add(competing);
                seam.Racing.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, seam.Proposal.Id, sequence: 0));
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
    public async Task A_recorded_input_removed_at_the_claim_seam_is_refused()
    {
        await AssertSeamRefusalAsync(
            "input removed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM attempt_input_messages WHERE AttemptId = {seam.Source.Id}"),
            Ineligible);
    }

    [Fact]
    public async Task A_recorded_input_replaced_by_another_proposal_at_the_claim_seam_is_refused_as_a_mismatch()
    {
        await AssertSeamRefusalAsync(
            "input replaced",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempt_input_messages SET CollaborationMessageId = {seam.Decoy!.Id} WHERE AttemptId = {seam.Source.Id}"),
            InputsMismatch,
            withDecoyRoot: true);
    }

    [Fact]
    public async Task A_second_recorded_input_added_at_the_claim_seam_is_refused_as_a_partial_identity()
    {
        await AssertSeamRefusalAsync(
            "input added",
            seam =>
            {
                seam.Racing.AttemptInputMessages.Add(
                    AttemptInputMessage.Record(Guid.NewGuid(), seam.Source.Id, seam.Decoy!.Id, sequence: 1));
                return Task.CompletedTask;
            },
            Ineligible,
            withDecoyRoot: true);
    }

    [Fact]
    public async Task The_reviewed_proposals_owning_attempt_failing_at_the_claim_seam_is_refused_despite_the_tracked_row()
    {
        await AssertSeamRefusalAsync(
            "proposal owner failed",
            async seam => await seam.Racing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET Status = 'Failed', AgentOutcome = 'InvalidStructuredOutput' WHERE Id = {seam.Proposal.AttemptId}"),
            "agent_attempts.proposal_attempt_not_valid");
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
                    ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
                    ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Acceptance,
                    seam.Proposal.Id, "Accepted.", JsonSerializer.Serialize(new { rationale = "Sound." }),
                    CollaborationMessageProvenance.ProviderObserved, RepairTestScene.Now));
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
            "runs.not_active");
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
    public async Task A_stop_threshold_change_committed_at_the_claim_seam_is_refused_by_the_policy_guard()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ => await TokenStopTestSupport.SetStopAsync(
                _fixture, scene.Run.Id, AgentProvider.ClaudeCode, 1_000_000),
        };

        var result = await NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrentTokenStopPolicy.PolicyChangedDuringClaimCode, Code(result));
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Concurrent_repair_claims_of_one_source_commit_exactly_one_repair()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
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
            NewHandler(firstContext, Reader(), firstStore)
                .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None),
            NewHandler(secondContext, Reader(), secondStore)
                .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None));

        var winners = results.Where(r => r.IsSuccess).ToList();
        var losers = results.Where(r => r.IsFailure).ToList();
        Assert.Single(winners);
        var loser = Assert.Single(losers);
        Assert.Contains(Code(loser), new[] { AlreadyRequested, NotLatest, "attempts.run_has_active_attempt" });

        await using var verify = _fixture.CreateContext();
        var repair = Assert.Single(verify.Attempts.Where(a => a.AgentRepairSourceAttemptId == source.Id));
        Assert.Equal(winners[0].Value.AttemptId, repair.Id);
        Assert.Equal(2, verify.Attempts.Count(a => a.RunId == scene.Run.Id && a.Kind == AttemptKind.Agent && a.AgentRole == AgentRole.CriticalReviewer));

        // Exactly the loser's sealed manifest was removed; the winner's is preserved.
        var deleted = firstStore.DeletedSealedFiles.Concat(secondStore.DeletedSealedFiles).ToList();
        var removed = Assert.Single(deleted);
        Assert.NotEqual(repair.Id, removed.AttemptId);
        Assert.Single(verify.Artifacts.Where(a => a.RunId == scene.Run.Id && a.AttemptId == repair.Id));
    }

    // ---- durability and cancellation at the claim boundary

    private async Task<(RepairTestScene Scene, Attempt Source)> SeedSimpleAsync()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        return (scene, source);
    }

    [Fact]
    public async Task A_transaction_that_cannot_be_acquired_fails_closed_and_removes_the_sealed_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { ThrowOnBeginTransaction = true };

        var result = await NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

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

        await Assert.ThrowsAsync<OperationCanceledException>(() => NewHandler(
                faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None));

        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task A_commit_that_fails_without_persisting_is_a_safe_failure_and_removes_the_sealed_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit };

        var result = await NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

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

        var result = await NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(source.Id, result.Value.RepairSourceAttemptId);
        Assert.Empty(store.DeletedSealedFiles);
        var attempts = await scene.AttemptsAsync();
        Assert.Contains(attempts, a => a.Id == result.Value.AttemptId && a.AgentRepairSourceAttemptId == source.Id);
    }

    [Fact]
    public async Task An_ambiguous_commit_the_probe_cannot_resolve_is_reported_unresolved_and_never_deletes_the_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit };
        var probe = new FixedDurabilityProbe(AttemptDurabilityCheckResult.Unresolved);

        var result = await NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store, probe)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

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

        await Assert.ThrowsAsync<OperationCanceledException>(() => NewHandler(
                faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None));

        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Cancellation_after_the_commit_propagates_and_preserves_the_persisted_attempts_manifest()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.CancellationAfterCommit };

        await Assert.ThrowsAsync<OperationCanceledException>(() => NewHandler(
                faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None));

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

        var result = await NewHandler(faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

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

        await Assert.ThrowsAsync<OperationCanceledException>(() => NewHandler(
                faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None));

        // The uncommitted transaction was rolled back, so nothing persisted and the sealed file is removed.
        Assert.Single(store.DeletedSealedFiles);
        Assert.DoesNotContain(await scene.AttemptsAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Fact]
    public async Task Deleting_a_repaired_source_alone_is_refused_while_deleting_the_run_removes_both()
    {
        var (scene, source) = await SeedSimpleAsync();
        var store = new RepairArtifactStore();
        var repair = await NewHandler(scene.Db, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store)
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);
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

    [Fact]
    public async Task The_database_backstop_allows_at_most_one_repair_per_source_even_across_direct_inserts()
    {
        var (scene, source) = await SeedSimpleAsync();
        var proposalId = (await scene.Db.AttemptInputMessages.SingleAsync(m => m.AttemptId == source.Id)).CollaborationMessageId;
        await using var context = _fixture.CreateContext();
        for (var index = 0; index < 2; index++)
        {
            var number = 50 + index;
            context.Attempts.Add(Attempt.ClaimAgentCriticalReviewWithModelRequest(
                Guid.NewGuid(), scene.Run.Id, number, scene.Workspace.Id, scene.Checkpoint.Id, RepairTestScene.Fingerprint,
                Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now, null, null, number, source.Id));
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.NotEqual(Guid.Empty, proposalId);
    }
}
