using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The one manual Codex Planner format-repair claim: it reuses the ordinary planning claim
/// (same protections, same handler) and adds a source link, source-eligibility checks at the
/// request and at the durable claim boundary, and a fixed manifest reminder. Every failure below
/// must create no Attempt and leave no orphaned sealed manifest.
/// </summary>
public sealed partial class CreateCodexPlanningAttemptCommandHandlerTests
{
    private const string NotFound = "agent_attempts.repair_source_not_found";
    private const string Ineligible = "agent_attempts.repair_source_ineligible";
    private const string RepairOfRepair = "agent_attempts.repair_of_repair_forbidden";
    private const string AlreadyRequested = "agent_attempts.repair_already_requested";
    private const string NotLatest = "agent_attempts.repair_source_not_latest";
    private const string CheckpointMismatch = "agent_attempts.repair_source_checkpoint_mismatch";

    private static readonly GitWorkspaceEvidenceResult MatchingEvidence =
        new(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null);

    private static Attempt ClaimPlanner(Run run, GitWorkspace workspace, GitCheckpoint checkpoint, int number) =>
        Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, number, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, number);

    private static Attempt CompleteInvalid(Attempt attempt)
    {
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(
            AgentOutcome.InvalidStructuredOutput, Fingerprint, Now,
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromSeconds(1)));
        return attempt;
    }

    private static Attempt InvalidPlanner(Run run, GitWorkspace workspace, GitCheckpoint checkpoint, int number) =>
        CompleteInvalid(ClaimPlanner(run, workspace, checkpoint, number));

    private static Attempt FailedPlanner(Run run, GitWorkspace workspace, GitCheckpoint checkpoint, int number)
    {
        var attempt = ClaimPlanner(run, workspace, checkpoint, number);
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        return attempt;
    }

    private static Attempt ClaimRepair(
        Run run, GitWorkspace workspace, GitCheckpoint checkpoint, int number, Attempt source) =>
        Attempt.ClaimAgentPlanningRepair(
            Guid.NewGuid(), run.Id, number, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, null, null, number, source.Id);

    private async Task<(Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint, Attempt Source)> SeedWithInvalidSourceAsync(
        DevalCopilotDbContext dbContext,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool codexObserved = true,
        int maximumAgentAttempts = 16,
        TimeSpan? maximumAgentInvocationTime = null)
    {
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(
            dbContext, claimRun: true, workspaceReady: workspaceReady, leaseActive: leaseActive,
            codexObserved: codexObserved, maximumAgentAttempts: maximumAgentAttempts,
            maximumAgentInvocationTime: maximumAgentInvocationTime);
        var source = InvalidPlanner(run, workspace, checkpoint, 1);
        dbContext.Attempts.Add(source);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return (run, workspace, checkpoint, source);
    }

    private async Task<(Run Run, Attempt Source)> SeedSecondRunWithInvalidSourceAsync(DevalCopilotDbContext dbContext)
    {
        var project = Project.Register(Guid.NewGuid(), "Other", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Another objective", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var source = InvalidPlanner(run, workspace, checkpoint, 1);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        dbContext.Attempts.Add(source);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return (run, source);
    }

    private CreateCodexPlanningAttemptCommandHandler NewHandler(
        DevalCopilotDbContext dbContext, FakeArtifactStore artifactStore, IGitWorkspaceEvidenceReader? evidenceReader = null) =>
        new(
            dbContext, evidenceReader ?? FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore,
            new FixedTimeProvider(Now), DurabilityProbe);

    private async Task AssertNothingClaimedAsync(Guid runId, int expectedAttemptCount, FakeArtifactStore artifactStore)
    {
        await using var verify = _fixture.CreateContext();
        Assert.Equal(expectedAttemptCount, await verify.Attempts.CountAsync(a => a.RunId == runId));
        Assert.Empty(artifactStore.DeletedSealedFiles);
    }

    [Fact]
    public async Task Repair_claims_a_linked_read_only_planner_attempt_and_consumes_the_next_budget_slot()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace, checkpoint, source) = await SeedWithInvalidSourceAsync(dbContext);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.AttemptNumber);
        Assert.Equal(source.Id, result.Value.RepairSourceAttemptId);

        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal(source.Id, repair.AgentRepairSourceAttemptId);
        Assert.Equal(AttemptStatus.Running, repair.Status);
        Assert.Equal(AgentProvider.Codex, repair.AgentProvider);
        Assert.Equal(AgentRole.Planner, repair.AgentRole);
        Assert.Equal(AgentResponseContract.Proposal, repair.AgentResponseContract);
        Assert.Equal(AgentPermissionProfile.ReadOnly, repair.AgentPermissionProfile);
        Assert.Equal("codex-planning-v1", repair.AgentAdapterContractVersion);
        Assert.Equal(2, repair.AgentBudgetSlot);
        Assert.Equal(workspace.Id, repair.AgentGitWorkspaceId);
        Assert.Equal(checkpoint.Id, repair.AgentGitCheckpointId);
        Assert.Equal(TimeSpan.FromMinutes(10), repair.AgentTimeout);

        var manifestArtifact = await verify.Artifacts.SingleAsync(
            a => a.AttemptId == repair.Id && a.Purpose == ArtifactPurpose.AgentContextManifest);
        Assert.Equal(repair.AgentContextManifestArtifactId, manifestArtifact.Id);

        // The source is untouched: still the same terminal invalid attempt, never itself linked.
        var persistedSource = await verify.Attempts.SingleAsync(a => a.Id == source.Id);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, persistedSource.AgentOutcome);
        Assert.Null(persistedSource.AgentRepairSourceAttemptId);

        // A claim never records a Proposal or any collaboration fact by itself.
        Assert.Empty(verify.CollaborationMessages.Where(m => m.RunId == run.Id));
        Assert.Empty(artifactStore.DeletedSealedFiles);
    }

    [Fact]
    public async Task Repair_manifest_is_the_ordinary_context_plus_one_fixed_reminder_and_nothing_from_the_source()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, _, _, source) = await SeedWithInvalidSourceAsync(dbContext);
        var artifactStore = new FakeArtifactStore();

        var repairResult = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);
        Assert.True(repairResult.IsSuccess);
        var repairManifest = File.ReadAllText(
            artifactStore.GetPartialPath(run.Id, repairResult.Value.AttemptId, ArtifactPurpose.AgentContextManifest));

        // An ordinary claim on a separate run gives the reference document shape.
        var otherFixture = new SqliteDatabaseFixture();
        await otherFixture.InitializeAsync();
        await using var otherContext = otherFixture.CreateContext();
        var (_, otherRun, _, _) = await SeedEligibleRunAsync(otherContext, claimRun: true);
        var otherStore = new FakeArtifactStore();
        var ordinaryResult = await new CreateCodexPlanningAttemptCommandHandler(
                otherContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), otherStore,
                new FixedTimeProvider(Now), new AttemptDurabilityProbe(otherFixture.Options))
            .HandleAsync(new CreateCodexPlanningAttemptCommand(otherRun.Id), CancellationToken.None);
        Assert.True(ordinaryResult.IsSuccess);
        var ordinaryManifest = File.ReadAllText(
            otherStore.GetPartialPath(otherRun.Id, ordinaryResult.Value.AttemptId, ArtifactPurpose.AgentContextManifest));
        await otherFixture.DisposeAsync();

        var ordinaryKeys = JsonDocument.Parse(ordinaryManifest).RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        using var repairDocument = JsonDocument.Parse(repairManifest);
        var repairKeys = repairDocument.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("formatRepairNotice", ordinaryKeys);
        Assert.Equal(ordinaryKeys.Append("formatRepairNotice"), repairKeys);
        var notice = repairDocument.RootElement.GetProperty("formatRepairNotice").GetString();
        Assert.Contains("failed structural validation", notice);
        Assert.Contains("fresh planning request", notice);
        Assert.Contains("unchanged expectedProposalSchema", notice);
        Assert.Equal("Plan the next increment", repairDocument.RootElement.GetProperty("objective").GetString());
        Assert.Equal(JsonValueKind.Null, repairDocument.RootElement.GetProperty("unresolvedHumanInstruction").ValueKind);
        Assert.Empty(repairDocument.RootElement.GetProperty("priorDecisionMessageIds").EnumerateArray());

        // Nothing identifying or descriptive about the source, its artifacts, or its outcome.
        Assert.DoesNotContain(source.Id.ToString(), repairManifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(source.AgentContextManifestArtifactId!.Value.ToString(), repairManifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidStructuredOutput", repairManifest);
        Assert.DoesNotContain("sealed", repairManifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repair_snapshots_the_preference_current_at_the_claim_boundary_after_a_change_during_external_work()
    {
        await using var seedContext = _fixture.CreateContext();
        var (run, _, _, source) = await SeedWithInvalidSourceAsync(seedContext);
        run.SetRequestedCodexAssignment("gpt-5-legacy", "low");
        await seedContext.SaveChangesAsync(CancellationToken.None);

        await using var raceContext = _fixture.CreateContext();
        await using var handlerContext = _fixture.CreateContext();
        var evidenceReader = new RaceInjectingEvidenceReader(MatchingEvidence, async cancellationToken =>
        {
            var racingRun = await raceContext.Runs.SingleAsync(candidate => candidate.Id == run.Id, cancellationToken);
            racingRun.SetRequestedCodexAssignment("gpt-6-sol", "high");
            await raceContext.SaveChangesAsync(cancellationToken);
        });

        var result = await NewHandler(handlerContext, new FakeArtifactStore(), evidenceReader)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal("gpt-6-sol", repair.AgentRequestedModel);
        Assert.Equal("high", repair.AgentRequestedEffort);
    }

    [Fact]
    public async Task Repair_fails_for_an_unknown_source_without_claiming_anything()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, _, _, _) = await SeedWithInvalidSourceAsync(dbContext);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NotFound, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 1, artifactStore);
    }

    [Fact]
    public async Task Repair_treats_another_runs_attempt_exactly_like_an_unknown_one()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, _, _, _) = await SeedWithInvalidSourceAsync(dbContext);
        var (foreignRun, foreignSource) = await SeedSecondRunWithInvalidSourceAsync(dbContext);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, foreignSource.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NotFound, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 1, artifactStore);
        await AssertNothingClaimedAsync(foreignRun.Id, 1, artifactStore);
    }

    [Fact]
    public async Task Repair_rejects_a_source_whose_outcome_is_not_invalid_structured_output()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        var source = FailedPlanner(run, workspace, checkpoint, 1);
        dbContext.Attempts.Add(source);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(Ineligible, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 1, artifactStore);
    }

    [Fact]
    public async Task Repair_rejects_a_claude_attempt_even_with_an_invalid_structured_outcome()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        var review = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        dbContext.Attempts.Add(CompleteInvalid(review));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, review.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(Ineligible, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 1, artifactStore);
    }

    [Fact]
    public async Task Repair_rejects_a_persisted_completed_row_with_an_invalid_structured_outcome()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        var source = InvalidPlanner(run, workspace, checkpoint, 1);
        typeof(Attempt).GetProperty(nameof(Attempt.Status))!.SetValue(source, AttemptStatus.Completed);
        dbContext.Attempts.Add(source);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(Ineligible, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 1, artifactStore);
    }

    // The newest checkpoint is valid and fresh evidence matches it, so an ordinary claim would
    // succeed; a repair must still be about the source's own workspace, checkpoint, and fingerprint.
    [Fact]
    public async Task Repair_rejects_a_newer_valid_checkpoint_that_differs_from_the_sources_and_leaves_no_manifest()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace, _, source) = await SeedWithInvalidSourceAsync(dbContext);
        var newerFingerprint = new string('b', 64);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), newerFingerprint, []));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var artifactStore = new FakeArtifactStore();
        var handler = NewHandler(dbContext, artifactStore, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(newerFingerprint));

        var repair = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(repair.IsFailure);
        Assert.Equal(CheckpointMismatch, Assert.Single(repair.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 1, artifactStore);
        await using (var verify = _fixture.CreateContext())
        {
            Assert.Empty(verify.Artifacts.Where(a => a.RunId == run.Id && a.Purpose == ArtifactPurpose.AgentContextManifest));
        }

        // The ordinary request on the same newer checkpoint stays available.
        var ordinary = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        Assert.True(ordinary.IsSuccess);
    }

    [Fact]
    public async Task Repair_rejects_a_source_that_is_no_longer_the_latest_agent_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace, checkpoint, source) = await SeedWithInvalidSourceAsync(dbContext);
        dbContext.Attempts.Add(FailedPlanner(run, workspace, checkpoint, 2));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NotLatest, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 2, artifactStore);
    }

    [Fact]
    public async Task Repair_rejects_a_source_while_a_newer_agent_attempt_is_still_running()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace, checkpoint, source) = await SeedWithInvalidSourceAsync(dbContext);
        dbContext.Attempts.Add(ClaimPlanner(run, workspace, checkpoint, 2));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NotLatest, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 2, artifactStore);
    }

    [Fact]
    public async Task Repair_fails_while_a_non_agent_attempt_is_running_for_the_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, _, _, source) = await SeedWithInvalidSourceAsync(dbContext);
        dbContext.Attempts.Add(Attempt.Claim(Guid.NewGuid(), run.Id, 2, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.run_has_active_attempt", Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 2, artifactStore);
    }

    [Fact]
    public async Task Repair_of_a_repair_is_forbidden_and_the_original_source_reports_already_repaired()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace, checkpoint, source) = await SeedWithInvalidSourceAsync(dbContext);
        var repair = CompleteInvalid(ClaimRepair(run, workspace, checkpoint, 2, source));
        dbContext.Attempts.Add(repair);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var artifactStore = new FakeArtifactStore();
        var handler = NewHandler(dbContext, artifactStore);

        var ofRepair = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, repair.Id), CancellationToken.None);
        var ofSource = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.Equal(RepairOfRepair, Assert.Single(ofRepair.Errors).Code);
        Assert.Equal(AlreadyRequested, Assert.Single(ofSource.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 2, artifactStore);
    }

    [Fact]
    public async Task An_ordinary_request_stays_available_after_an_invalid_source_and_after_a_repair()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace, checkpoint, source) = await SeedWithInvalidSourceAsync(dbContext);
        var artifactStore = new FakeArtifactStore();

        var afterInvalid = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        Assert.True(afterInvalid.IsSuccess);
        Assert.Null(afterInvalid.Value.RepairSourceAttemptId);

        await using var verify = _fixture.CreateContext();
        var ordinary = await verify.Attempts.SingleAsync(a => a.Id == afterInvalid.Value.AttemptId);
        Assert.Null(ordinary.AgentRepairSourceAttemptId);
        Assert.NotEqual(source.Id, ordinary.Id);
        _ = (workspace, checkpoint);
    }

    [Theory]
    [InlineData("count", "agent_attempts.budget_exhausted")]
    [InlineData("time", "agent_attempts.time_budget_exceeded")]
    [InlineData("provider", "agent_attempts.provider_not_observed")]
    [InlineData("lease", "agent_attempts.lease_not_active")]
    [InlineData("workspace", "agent_attempts.workspace_not_ready")]
    public async Task Repair_applies_the_ordinary_claim_gates(string gate, string expectedCode)
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, _, _, source) = await SeedWithInvalidSourceAsync(
            dbContext,
            workspaceReady: gate != "workspace",
            leaseActive: gate != "lease",
            codexObserved: gate != "provider",
            maximumAgentAttempts: gate == "count" ? 1 : 16,
            maximumAgentInvocationTime: gate == "time" ? TimeSpan.FromMinutes(1) : null);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 1, artifactStore);
    }

    [Fact]
    public async Task Repair_requires_the_current_git_checkpoint_fingerprint()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, _, _, source) = await SeedWithInvalidSourceAsync(dbContext);
        var artifactStore = new FakeArtifactStore();

        var result = await NewHandler(dbContext, artifactStore, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(new string('b', 64)))
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 1, artifactStore);
    }

    // The source is still eligible when the request begins and stops being the latest Agent
    // attempt during the external evidence capture. Only the guard at the durable claim boundary
    // can catch that: no repair is committed and the already-sealed manifest is removed.
    [Fact]
    public async Task A_source_made_stale_during_the_claim_is_rejected_at_the_commit_boundary_and_the_manifest_is_removed()
    {
        await using var seedContext = _fixture.CreateContext();
        var (run, workspace, checkpoint, source) = await SeedWithInvalidSourceAsync(seedContext);
        await using var raceContext = _fixture.CreateContext();
        await using var handlerContext = _fixture.CreateContext();
        var artifactStore = new FakeArtifactStore();
        var evidenceReader = new RaceInjectingEvidenceReader(MatchingEvidence, async cancellationToken =>
        {
            raceContext.Attempts.Add(FailedPlanner(run, workspace, checkpoint, 2));
            await raceContext.SaveChangesAsync(cancellationToken);
        });

        var result = await NewHandler(handlerContext, artifactStore, evidenceReader)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NotLatest, Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(2, await verify.Attempts.CountAsync(a => a.RunId == run.Id));
        Assert.Empty(verify.Attempts.Where(a => a.AgentRepairSourceAttemptId != null));
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
    }

    // A competing repair committed during this claim's external work: this claim reports the
    // source as already repaired, commits nothing, and removes its sealed manifest.
    [Fact]
    public async Task A_competing_repair_committed_during_the_claim_leaves_exactly_one_repair_and_no_orphaned_manifest()
    {
        await using var seedContext = _fixture.CreateContext();
        var (run, workspace, checkpoint, source) = await SeedWithInvalidSourceAsync(seedContext);
        var winner = ClaimRepair(run, workspace, checkpoint, 2, source);
        await using var raceContext = _fixture.CreateContext();
        await using var handlerContext = _fixture.CreateContext();
        var artifactStore = new FakeArtifactStore();
        var evidenceReader = new RaceInjectingEvidenceReader(MatchingEvidence, async cancellationToken =>
        {
            raceContext.Attempts.Add(winner);
            await raceContext.SaveChangesAsync(cancellationToken);
        });

        var result = await NewHandler(handlerContext, artifactStore, evidenceReader)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlreadyRequested, Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.SingleAsync(a => a.AgentRepairSourceAttemptId == source.Id);
        Assert.Equal(winner.Id, repair.Id);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
    }

    [Fact]
    public async Task Two_concurrent_repair_claims_for_one_source_commit_exactly_one_repair()
    {
        await using var seedContext = _fixture.CreateContext();
        var (run, _, _, source) = await SeedWithInvalidSourceAsync(seedContext);
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var firstStore = new FakeArtifactStore();
        var secondStore = new FakeArtifactStore();
        var command = new CreateCodexPlanningAttemptCommand(run.Id, source.Id);

        var results = await Task.WhenAll(
            Task.Run(() => NewHandler(firstContext, firstStore).HandleAsync(command, CancellationToken.None)),
            Task.Run(() => NewHandler(secondContext, secondStore).HandleAsync(command, CancellationToken.None)));

        var winner = Assert.Single(results, r => r.IsSuccess);
        var loser = Assert.Single(results, r => r.IsFailure);
        Assert.Equal(AlreadyRequested, Assert.Single(loser.Errors).Code);

        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.SingleAsync(a => a.AgentRepairSourceAttemptId == source.Id);
        Assert.Equal(winner.Value.AttemptId, repair.Id);
        Assert.Equal(2, await verify.Attempts.CountAsync(a => a.RunId == run.Id));

        // A loser rejected at the request-time check never sealed a manifest; one that reached the claim boundary removes only its own, and the winner's stays referenced.
        var deleted = firstStore.DeletedSealedFiles.Concat(secondStore.DeletedSealedFiles).ToArray();
        Assert.True(deleted.Length <= 1);
        Assert.DoesNotContain(deleted, entry => entry.AttemptId == winner.Value.AttemptId);
    }

    // ---- Run-scoped Codex token-activity stop on the planning claims -------------------------

    private static Attempt InvalidPlannerWithUsage(
        Run run, GitWorkspace workspace, GitCheckpoint checkpoint, int number, AgentTokenUsageEvidence usage)
    {
        var attempt = ClaimPlanner(run, workspace, checkpoint, number);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(
            AgentOutcome.InvalidStructuredOutput, Fingerprint, Now,
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromSeconds(1)), usage);
        return attempt;
    }

    private async Task<(Guid RunId, Guid SourceId)> SeedUsageSourceAsync(int maximumAgentAttempts = 16)
    {
        await using var context = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(
            context, claimRun: true, maximumAgentAttempts: maximumAgentAttempts);
        var source = InvalidPlannerWithUsage(run, workspace, checkpoint, 1, TokenStopTestSupport.CodexUsage(1000, 200));
        context.Attempts.Add(source);
        await context.SaveChangesAsync(CancellationToken.None);
        return (run.Id, source.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_reached_codex_stop_refuses_ordinary_and_repair_planning_claims_before_any_external_work(bool repair)
    {
        var (runId, sourceId) = await SeedUsageSourceAsync();
        await TokenStopTestSupport.SetStopAsync(_fixture, runId, AgentProvider.Codex, 1200);
        var reader = new TokenStopTestSupport.CountingEvidenceReader(MatchingEvidence);
        var artifactStore = new FakeArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await NewHandler(context, artifactStore, reader)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(runId, repair ? sourceId : null), CancellationToken.None);

        Assert.Equal(AgentTokenStopGate.ReachedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, reader.Calls);
        await AssertNothingClaimedAsync(runId, 1, artifactStore);
    }

    [Fact]
    public async Task A_repair_claim_below_the_codex_stop_is_permitted_and_counts_toward_it_afterwards()
    {
        var (runId, sourceId) = await SeedUsageSourceAsync();
        await TokenStopTestSupport.SetStopAsync(_fixture, runId, AgentProvider.Codex, 1201);
        await using var context = _fixture.CreateContext();

        var result = await NewHandler(context, new FakeArtifactStore())
            .HandleAsync(new CreateCodexPlanningAttemptCommand(runId, sourceId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(sourceId, (await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId)).AgentRepairSourceAttemptId);
    }

    [Fact]
    public async Task A_repair_claim_whose_source_has_no_recorded_usage_cannot_prove_the_codex_stop_and_is_refused()
    {
        await using var seedContext = _fixture.CreateContext();
        var (run, _, _, source) = await SeedWithInvalidSourceAsync(seedContext);
        await TokenStopTestSupport.SetStopAsync(_fixture, run.Id, AgentProvider.Codex, 1_000_000);
        var artifactStore = new FakeArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await NewHandler(context, artifactStore)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, Assert.Single(result.Errors).Code);
        await AssertNothingClaimedAsync(run.Id, 1, artifactStore);
    }

    [Fact]
    public async Task A_stop_change_at_the_repair_claim_commit_creates_no_repair_and_leaves_the_source_eligible()
    {
        var (runId, sourceId) = await SeedUsageSourceAsync();
        await TokenStopTestSupport.SetStopAsync(_fixture, runId, AgentProvider.Codex, 1201);
        var artifactStore = new FakeArtifactStore();
        var reader = new RaceInjectingEvidenceReader(
            MatchingEvidence, _ => TokenStopTestSupport.SetStopAsync(_fixture, runId, AgentProvider.Codex, 1200));
        await using var context = _fixture.CreateContext();

        var result = await NewHandler(context, artifactStore, reader)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(runId, sourceId), CancellationToken.None);

        Assert.Equal(CurrentTokenStopPolicy.PolicyChangedDuringClaimCode, Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.Attempts.CountAsync(a => a.RunId == runId));
        Assert.Empty(verify.Artifacts.Where(a => a.RunId == runId && verify.Attempts.All(x => x.Id != a.AttemptId)));
    }

    [Fact]
    public async Task A_created_run_is_claimed_under_a_configured_codex_stop_with_no_dispatched_history()
    {
        await using var seedContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(seedContext, claimRun: false);
        await TokenStopTestSupport.SetStopAsync(_fixture, run.Id, AgentProvider.Codex, 1);
        await using var context = _fixture.CreateContext();

        var result = await NewHandler(context, new FakeArtifactStore())
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var claimedRun = await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(RunLifecycle.Running, claimedRun.Lifecycle);
        Assert.Equal(1, claimedRun.CodexTokenStopThreshold);
    }
}
