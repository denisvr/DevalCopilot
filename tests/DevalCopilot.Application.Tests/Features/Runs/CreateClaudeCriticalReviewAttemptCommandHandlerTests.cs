using System.Security.Cryptography;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Mirrors <c>CreateCodexPlanningAttemptCommandHandlerTests</c>'s fixture/fake style
/// exactly, adapted for the additional reviewed-Proposal validation chain this handler alone
/// has.</summary>
public sealed class CreateClaudeCriticalReviewAttemptCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeGitWorkspaceEvidenceReader(GitWorkspaceEvidenceResult result) : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public static FakeGitWorkspaceEvidenceReader MatchingCheckpoint(string fingerprintSha256) => new(
            new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null));
    }

    private sealed class RaceInjectingEvidenceReader(GitWorkspaceEvidenceResult result, Func<CancellationToken, Task> injectRace)
        : IGitWorkspaceEvidenceReader
    {
        private bool _injected;

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            if (!_injected)
            {
                _injected = true;
                await injectRace(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>A minimal, deterministic in-memory fake — mirrors the Codex planning template's
    /// <c>FakeArtifactStore</c> exactly.</summary>
    private sealed class FakeArtifactStore : IArtifactStore
    {
        private readonly Dictionary<string, byte[]> _partialContent = new(StringComparer.Ordinal);

        private static readonly string PartialRoot = Path.Combine(Path.GetTempPath(), "devalcopilot-app-tests-partials");

        public bool SealShouldFail { get; set; }

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            Path.Combine(PartialRoot, $"{runId:N}", $"{attemptId:N}", $"{purpose}.partial");

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            $"{runId:N}/{attemptId:N}/{purpose}.sealed";

        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken)
        {
            if (SealShouldFail)
            {
                return Task.FromResult<SealedOutputFile?>(null);
            }

            var partialPath = GetPartialPath(runId, attemptId, purpose);
            var bytes = _partialContent.TryGetValue(partialPath, out var written) ? written : [];
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            return Task.FromResult<SealedOutputFile?>(
                new SealedOutputFile(GetSealedRelativePath(runId, attemptId, purpose), bytes.LongLength, hash));
        }

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;

        public Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            Task.FromResult<SealedOutputFile?>(null);

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose)
        {
        }

        public HashSet<(Guid RunId, Guid AttemptId, ArtifactPurpose Purpose)> DeletedSealedFiles { get; } = new();

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            DeletedSealedFiles.Add((runId, attemptId, purpose));

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(new PartialReadWindow(string.Empty, fromOffset, 0));

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0));
    }

    private async Task<(Project Project, Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint)> SeedEligibleRunAsync(
        DevalCopilotDbContext dbContext,
        bool claimRun = false,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool hasCheckpoint = true,
        bool claudeObserved = true,
        CapabilityLaunchKind claudeLaunchKind = CapabilityLaunchKind.DirectExecutable,
        bool seedClaudeCapability = true)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        if (claimRun)
        {
            run.Claim(Now);
        }

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        if (workspaceReady)
        {
            workspace.MarkReady();
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);

        if (hasCheckpoint)
        {
            var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
            dbContext.GitCheckpoints.Add(checkpoint);
        }

        // A distinct physical identity per call: some tests seed more than one eligible run in
        // the same database, and the Active-lease unique constraint is keyed by
        // (PhysicalVolumeSerialNumber, PhysicalFileId) — a fixed identity here would collide.
        // Guid.NewGuid() is used only to generate a unique test identity, never for security.
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), Now);
        if (!leaseActive)
        {
            lease.Release(Now);
        }

        dbContext.RepositoryMutationLeases.Add(lease);

        if (seedClaudeCapability)
        {
            var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
            if (claudeObserved)
            {
                claude.MarkDispatched(Now);
                var resolvedScriptPath = claudeLaunchKind == CapabilityLaunchKind.NodeScript ? @"C:\safe\claude.js" : null;
                claude.RecordSuccess(claudeLaunchKind, @"C:\safe\claude.exe", resolvedScriptPath, "1.2.3", Now, Now.AddMinutes(5));
            }

            dbContext.HostCapabilitySnapshots.Add(claude);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var checkpointEntity = hasCheckpoint
            ? await dbContext.GitCheckpoints.SingleAsync(c => c.WorkspaceId == workspace.Id)
            : null;

        return (project, run, workspace, checkpointEntity!);
    }

    /// <summary>Seeds a completed Codex Planner attempt bound to the given workspace/checkpoint and
    /// the Proposal collaboration message it produced — the reviewed evidence every critical-review
    /// creation test needs. Returns both so tests can independently mutate either the message or its
    /// owning attempt to exercise each validation branch.</summary>
    private static (Attempt PlanningAttempt, CollaborationMessage ProposalMessage) SeedCompletedProposal(
        Guid runId,
        Guid workspaceId,
        Guid checkpointId,
        string checkpointFingerprintSha256,
        DateTimeOffset occurredAtUtc)
    {
        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, checkpointFingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, occurredAtUtc);
        planningAttempt.MarkAgentDispatched(occurredAtUtc);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, checkpointFingerprintSha256, occurredAtUtc, processEvidence: TestProcessEvidence.CleanExit);

        var proposalMessage = CollaborationMessage.Record(
            Guid.NewGuid(),
            runId,
            planningAttempt.Id,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal,
            null,
            "Add the ledger table and its query.",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table then the query",
                risks = "Unbounded content",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }),
            CollaborationMessageProvenance.ProviderObserved,
            occurredAtUtc);

        return (planningAttempt, proposalMessage);
    }

    [Fact]
    public async Task HandleAsync_creates_a_critical_review_attempt_bound_to_the_reviewed_proposal_and_claims_a_created_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: false);
        var (planningAttempt, proposalMessage) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(proposalMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposalMessage.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.AttemptNumber);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);

        var attempt = Assert.Single(dbContext.Attempts, a => a.Id == result.Value.AttemptId);
        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AgentProvider.ClaudeCode, attempt.AgentProvider);
        Assert.Equal(AgentRole.CriticalReviewer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.CriticalReview, attempt.AgentResponseContract);
        var inputMessage = Assert.Single(dbContext.AttemptInputMessages, m => m.AttemptId == attempt.Id);
        Assert.Equal(proposalMessage.Id, inputMessage.CollaborationMessageId);
        Assert.Equal(0, inputMessage.Sequence);
        Assert.Equal(workspace.Id, attempt.AgentGitWorkspaceId);
        Assert.Equal(checkpoint.Id, attempt.AgentGitCheckpointId);
        Assert.Equal(Fingerprint, attempt.AgentCheckpointFingerprintSha256);

        var manifestArtifact = Assert.Single(
            dbContext.Artifacts, a => a.AttemptId == attempt.Id && a.Purpose == ArtifactPurpose.AgentContextManifest);
        Assert.Equal(attempt.AgentContextManifestArtifactId, manifestArtifact.Id);
        Assert.Equal("application/json", manifestArtifact.MediaType);
    }

    // Discriminating regression for Slice B.1: production's ClaimAgent factory always fixes the
    // Planner role to Codex, so this substitutes the owning attempt's provider via reflection
    // (AttemptProviderSubstitution — a test-only helper, never a production path) purely to prove
    // this handler's eligibility gate is authorized by AgentRole alone. Before this correction, the
    // gate compared the owning attempt's AgentProvider (and the message's Actor) to fixed Codex
    // literals, which would have rejected this exact scenario.
    [Fact]
    public async Task HandleAsync_accepts_a_planner_proposal_from_the_alternate_provider()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: false);
        var (planningAttempt, proposalMessage) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        AttemptProviderSubstitution.SetProvider(planningAttempt, AgentProvider.ClaudeCode);
        var alternateProviderProposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Proposal, null,
            proposalMessage.Summary, proposalMessage.StructuredContentJson, CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(alternateProviderProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, alternateProviderProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // The inverse: the normal provider assigned to the WRONG role must still be rejected — proves
    // the gate is genuinely role-first, not merely provider-blind.
    [Fact]
    public async Task HandleAsync_rejects_a_proposal_owned_by_a_non_planner_attempt_even_from_the_normal_provider()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: false);
        var nonPlannerAttempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        nonPlannerAttempt.MarkAgentDispatched(Now);
        nonPlannerAttempt.CompleteAgent(AgentOutcome.Resolved, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        var wronglyTypedMessage = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, nonPlannerAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Not really a Planner proposal.",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Steps",
                risks = "Risks",
                verificationPlan = "Plan",
                escalationPoints = "None",
            }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.Attempts.Add(nonPlannerAttempt);
        dbContext.CollaborationMessages.Add(wronglyTypedMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, wronglyTypedMessage.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.proposal_attempt_not_valid", Assert.Single(result.Errors).Code);
    }

    // A message whose Actor does not match ParticipantIdentity.ForAgentWithUnknownRole(owningAttempt.AgentProvider)
    // must be rejected even though every other fact (role, contract, outcome) is otherwise valid.
    [Fact]
    public async Task HandleAsync_rejects_a_proposal_whose_actor_does_not_match_its_owning_attempts_provider()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: false);
        var (planningAttempt, proposalMessage) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        // planningAttempt.AgentProvider remains Codex, but the message's own Actor is
        // Claude — a divergence that must never be trusted.
        var mismatchedProposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Proposal, null,
            proposalMessage.Summary, proposalMessage.StructuredContentJson, CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(mismatchedProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, mismatchedProposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.proposal_attempt_not_valid", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_run_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_workspace_is_not_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, workspaceReady: false);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.workspace_not_ready", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_no_lease_is_active_for_the_workspace()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, leaseActive: false);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.lease_not_active", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_no_checkpoint_exists_for_the_workspace()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, hasCheckpoint: false);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_missing", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_an_attempt_is_already_running_for_the_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        dbContext.Attempts.Add(Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.run_has_active_attempt", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_claude_capability_was_never_observed_successfully()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, claudeObserved: false);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.provider_not_observed", Assert.Single(result.Errors).Code);
    }

    /// <summary>Unlike Codex (which accepts any observed launch kind), this handler requires
    /// exactly <see cref="CapabilityLaunchKind.DirectExecutable"/> for Claude — Claude is never
    /// treated as a Node script in this slice.</summary>
    [Fact]
    public async Task HandleAsync_fails_when_the_claude_capability_resolved_as_a_node_script_instead_of_a_direct_executable()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, claudeLaunchKind: CapabilityLaunchKind.NodeScript);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.provider_not_observed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_fresh_git_evidence_fingerprint_no_longer_matches_the_checkpoint()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);

        var evidenceReader = FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(new string('b', 64));
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(dbContext, evidenceReader, new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Attempts.Where(a => a.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_proposal_message_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.proposal_not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_proposal_message_belongs_to_a_different_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var (_, otherRun, otherWorkspace, otherCheckpoint) = await SeedEligibleRunAsync(dbContext, seedClaudeCapability: false);
        var (otherPlanningAttempt, otherProposal) = SeedCompletedProposal(
            otherRun.Id, otherWorkspace.Id, otherCheckpoint.Id, Fingerprint, Now);
        dbContext.Attempts.Add(otherPlanningAttempt);
        dbContext.CollaborationMessages.Add(otherProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, otherProposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.proposal_not_found", Assert.Single(result.Errors).Code);
        _ = workspace;
        _ = checkpoint;
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_message_is_not_a_proposal()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var (planningAttempt, _) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);

        var acceptanceMessage = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Acceptance, Guid.NewGuid(),
            "Not a proposal.", System.Text.Json.JsonSerializer.Serialize(new { rationale = "n/a" }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.CollaborationMessages.Add(acceptanceMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, acceptanceMessage.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.not_provider_observed_planner_proposal", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_proposal_is_simulated_rather_than_provider_observed()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var (planningAttempt, _) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);

        var simulatedProposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Simulated proposal.",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Steps",
                risks = "Risks",
                verificationPlan = "Plan",
                escalationPoints = "None",
            }),
            CollaborationMessageProvenance.Simulated, Now);
        dbContext.CollaborationMessages.Add(simulatedProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, simulatedProposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.not_provider_observed_planner_proposal", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_proposals_owning_attempt_did_not_complete_as_proposed()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);

        var failedPlanningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        failedPlanningAttempt.MarkAgentDispatched(Now);
        failedPlanningAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        dbContext.Attempts.Add(failedPlanningAttempt);

        // A Human-authored Proposal can still pass the message-level check (Actor == Codex is
        // required, so simulate the only way a Proposal message can exist without its owning
        // attempt having produced it as Proposed: directly record one against an attempt that
        // itself never completed as Proposed.
        var orphanedProposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, failedPlanningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Should never be reviewable.",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Steps",
                risks = "Risks",
                verificationPlan = "Plan",
                escalationPoints = "None",
            }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.CollaborationMessages.Add(orphanedProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, orphanedProposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.proposal_attempt_not_valid", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_owning_attempt_is_a_claude_critical_review_attempt_rather_than_a_codex_planner()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);

        var reviewAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        // Must not be left Running: the run-wide "at most one Running attempt" invariant would
        // otherwise reject this request with attempts.run_has_active_attempt before the handler
        // ever reaches the proposal-validation chain this test means to exercise.
        reviewAttempt.MarkAgentDispatched(Now);
        reviewAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        dbContext.Attempts.Add(reviewAttempt);

        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, reviewAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Should never be reviewable.",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Steps",
                risks = "Risks",
                verificationPlan = "Plan",
                escalationPoints = "None",
            }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.CollaborationMessages.Add(proposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.proposal_attempt_not_valid", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_proposal_was_made_against_a_stale_superseded_checkpoint()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, staleCheckpoint) = await SeedEligibleRunAsync(dbContext);

        // The workspace has since moved past the checkpoint the planning attempt (and its
        // Proposal) committed to — a newer checkpoint is now current for this workspace.
        var currentFingerprint = new string('c', 64);
        var currentCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 2, Now.AddMinutes(1), new string('c', 40), currentFingerprint, []);
        dbContext.GitCheckpoints.Add(currentCheckpoint);
        var (planningAttempt, proposalMessage) = SeedCompletedProposal(
            run.Id, workspace.Id, staleCheckpoint.Id, staleCheckpoint.FingerprintSha256, Now);
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(proposalMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(currentFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposalMessage.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.proposal_checkpoint_stale", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_proposal_was_made_against_a_different_workspace()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);

        // A newer, now-current workspace for the same project — the planning attempt (and its
        // Proposal) committed to the OLD workspace above, not this one.
        var currentWorkspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        currentWorkspace.MarkReady();
        dbContext.GitWorkspaces.Add(currentWorkspace);
        var currentFingerprint = new string('d', 64);
        var currentCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), currentWorkspace.Id, 1, Now, new string('d', 40), currentFingerprint, []);
        dbContext.GitCheckpoints.Add(currentCheckpoint);
        dbContext.RepositoryMutationLeases.Add(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, currentWorkspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));

        var (planningAttempt, proposalMessage) = SeedCompletedProposal(
            run.Id, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, Now);
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(proposalMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(currentFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposalMessage.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.proposal_checkpoint_stale", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_proposal_already_has_a_successful_accepted_review()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        var (planningAttempt, proposalMessage) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(proposalMessage);

        var priorReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        priorReview.MarkAgentDispatched(Now);
        priorReview.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Attempts.Add(priorReview);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), priorReview.Id, proposalMessage.Id, sequence: 0));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposalMessage.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.already_reviewed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_proposal_already_has_a_successful_challenged_review()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        var (planningAttempt, proposalMessage) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(proposalMessage);

        var priorReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        priorReview.MarkAgentDispatched(Now);
        priorReview.CompleteAgent(AgentOutcome.Challenged, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Attempts.Add(priorReview);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), priorReview.Id, proposalMessage.Id, sequence: 0));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposalMessage.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.already_reviewed", Assert.Single(result.Errors).Code);
    }

    /// <summary>A prior review attempt that failed (never reached Accepted/Challenged) never blocks
    /// a fresh review of the same proposal — only a genuinely successful prior review does.</summary>
    [Fact]
    public async Task HandleAsync_succeeds_when_a_prior_review_of_the_same_proposal_failed_rather_than_succeeded()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        var (planningAttempt, proposalMessage) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(proposalMessage);

        var priorFailedReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        priorFailedReview.MarkAgentDispatched(Now);
        priorFailedReview.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        dbContext.Attempts.Add(priorFailedReview);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), priorFailedReview.Id, proposalMessage.Id, sequence: 0));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposalMessage.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_fails_and_creates_no_attempt_when_sealing_the_context_manifest_fails()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var (planningAttempt, proposalMessage) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(proposalMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint),
            new FakeArtifactStore { SealShouldFail = true }, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposalMessage.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.context_manifest_seal_failed", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Attempts.Where(a => a.Id != planningAttempt.Id && a.RunId == run.Id));
    }

    // Reproduces the exact race the filtered database unique index exists to close: a competing
    // request commits its own Running attempt for this run strictly between this handler's own
    // pre-check and its own final SaveChangesAsync.
    [Fact]
    public async Task HandleAsync_resolves_a_lost_race_as_a_safe_conflict_with_no_orphaned_manifest()
    {
        await using var seedContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(seedContext, claimRun: true);
        var (planningAttempt, proposalMessage) = SeedCompletedProposal(run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        seedContext.Attempts.Add(planningAttempt);
        seedContext.CollaborationMessages.Add(proposalMessage);
        await seedContext.SaveChangesAsync(CancellationToken.None);
        var runId = run.Id;
        var proposalMessageId = proposalMessage.Id;

        await using var raceContext = _fixture.CreateContext();
        await using var handlerContext = _fixture.CreateContext();

        var artifactStore = new FakeArtifactStore();
        var evidenceReader = new RaceInjectingEvidenceReader(
            new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null),
            async cancellationToken =>
            {
                raceContext.Attempts.Add(Attempt.Claim(Guid.NewGuid(), runId, 2, Now));
                await raceContext.SaveChangesAsync(cancellationToken);
            });

        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(handlerContext, evidenceReader, artifactStore, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.run_has_active_attempt", Assert.Single(result.Errors).Code);

        await using var verifyContext = _fixture.CreateContext();
        var runningAttempts = await verifyContext.Attempts.Where(a => a.RunId == runId && a.Status == AttemptStatus.Running).ToListAsync();
        var survivor = Assert.Single(runningAttempts);
        Assert.Equal(AttemptKind.Simulated, survivor.Kind);
        Assert.Empty(verifyContext.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.CriticalReviewer));

        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
    }
}
