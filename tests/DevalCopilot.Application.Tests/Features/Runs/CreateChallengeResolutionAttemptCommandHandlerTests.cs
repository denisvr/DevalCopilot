using System.Security.Cryptography;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Mirrors <c>CreateClaudeCriticalReviewAttemptCommandHandlerTests</c>'s fixture/fake
/// style exactly, adapted for the additional Challenged-review/Proposal/Challenge-set validation
/// chain this handler alone has.</summary>
public sealed class CreateChallengeResolutionAttemptCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
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

    private sealed class FakeArtifactStore : IArtifactStore
    {
        private readonly Dictionary<string, byte[]> _partialContent = new(StringComparer.Ordinal);

        private static readonly string PartialRoot = Path.Combine(Path.GetTempPath(), "devalcopilot-app-tests-resolution-partials");

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
        bool workspaceReady = true,
        bool leaseActive = true,
        bool hasCheckpoint = true,
        bool codexObserved = true)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);

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

        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), Now);
        if (!leaseActive)
        {
            lease.Release(Now);
        }

        dbContext.RepositoryMutationLeases.Add(lease);

        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        if (codexObserved)
        {
            codex.MarkDispatched(Now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        }

        dbContext.HostCapabilitySnapshots.Add(codex);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var checkpointEntity = hasCheckpoint
            ? await dbContext.GitCheckpoints.SingleAsync(c => c.WorkspaceId == workspace.Id)
            : null;

        return (project, run, workspace, checkpointEntity!);
    }

    /// <summary>Seeds a complete, valid Challenged Claude critical-review chain: the owning Codex
    /// planning attempt, its Proposal, a completed Claude critical-review attempt bound to the
    /// exact given workspace/checkpoint, and its ordered Challenge set — the exact evidence
    /// <c>CreateChallengeResolutionAttemptCommandHandler</c> requires.</summary>
    private static (Attempt PlanningAttempt, CollaborationMessage Proposal, Attempt ReviewAttempt, List<CollaborationMessage> Challenges)
        SeedChallengedReview(
            DevalCopilotDbContext dbContext,
            Guid runId,
            Guid workspaceId,
            Guid checkpointId,
            string checkpointFingerprintSha256,
            DateTimeOffset occurredAtUtc,
            int challengeCount = 2)
    {
        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, checkpointFingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, occurredAtUtc);
        planningAttempt.MarkAgentDispatched(occurredAtUtc);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, checkpointFingerprintSha256, occurredAtUtc);
        dbContext.Attempts.Add(planningAttempt);

        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), runId, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
            "Add the ledger table and its query.",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table then the query",
                risks = "Unbounded content",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }),
            CollaborationMessageProvenance.ProviderObserved, occurredAtUtc);
        dbContext.CollaborationMessages.Add(proposal);

        var reviewAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, 2, workspaceId, checkpointId, checkpointFingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, occurredAtUtc);
        reviewAttempt.MarkAgentDispatched(occurredAtUtc);
        reviewAttempt.CompleteAgent(AgentOutcome.Challenged, checkpointFingerprintSha256, occurredAtUtc);
        dbContext.Attempts.Add(reviewAttempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), reviewAttempt.Id, proposal.Id, sequence: 0));

        var challenges = new List<CollaborationMessage>();
        for (var index = 0; index < challengeCount; index++)
        {
            var challenge = CollaborationMessage.Record(
                Guid.NewGuid(), runId, reviewAttempt.Id, CollaborationMessage.ProtocolVersionOne,
                ParticipantKind.Claude, ParticipantKind.Codex, CollaborationMessageType.Challenge, proposal.Id,
                $"Challenge {index + 1} summary",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    disputedItem = $"Disputed item {index + 1}",
                    materialImpact = $"Material impact {index + 1}",
                    reasoning = $"Reasoning {index + 1}",
                    alternativeOrQuestion = $"Alternative or question {index + 1}",
                }),
                CollaborationMessageProvenance.ProviderObserved, occurredAtUtc);
            dbContext.CollaborationMessages.Add(challenge);
            challenges.Add(challenge);
        }

        return (planningAttempt, proposal, reviewAttempt, challenges);
    }

    [Fact]
    public async Task HandleAsync_creates_a_challenge_resolution_attempt_bound_to_the_complete_ordered_input_set()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var (_, proposal, reviewAttempt, challenges) =
            SeedChallengedReview(dbContext, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, reviewAttempt.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);

        var attempt = Assert.Single(dbContext.Attempts, a => a.Id == result.Value.AttemptId);
        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AgentProvider.Codex, attempt.AgentProvider);
        Assert.Equal(AgentRole.Resolver, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.ChallengeResolution, attempt.AgentResponseContract);

        var orderedInputMessages = dbContext.AttemptInputMessages
            .Where(m => m.AttemptId == attempt.Id)
            .OrderBy(m => m.Sequence)
            .ToList();
        Assert.Equal(challenges.Count + 1, orderedInputMessages.Count);
        Assert.Equal(proposal.Id, orderedInputMessages[0].CollaborationMessageId);
        for (var index = 0; index < challenges.Count; index++)
        {
            Assert.Equal(challenges[index].Id, orderedInputMessages[index + 1].CollaborationMessageId);
        }

        var manifestArtifact = Assert.Single(
            dbContext.Artifacts, a => a.AttemptId == attempt.Id && a.Purpose == ArtifactPurpose.AgentContextManifest);
        Assert.Equal(attempt.AgentContextManifestArtifactId, manifestArtifact.Id);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_run_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_run_is_not_running()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_running", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_an_attempt_is_already_running_for_the_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        dbContext.Attempts.Add(Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.run_has_active_attempt", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_workspace_is_not_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, workspaceReady: false);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.workspace_not_ready", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_codex_capability_was_never_observed_successfully()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, codexObserved: false);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.provider_not_observed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_fresh_git_evidence_fingerprint_no_longer_matches_the_checkpoint()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(new string('b', 64)), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_challenged_review_attempt_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.challenged_review_not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_named_attempt_is_not_a_completed_challenged_review()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var acceptedReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        acceptedReview.MarkAgentDispatched(Now);
        acceptedReview.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now);
        dbContext.Attempts.Add(acceptedReview);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, acceptedReview.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.challenged_review_not_valid", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_challenged_review_was_made_against_a_stale_checkpoint()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, staleCheckpoint) = await SeedEligibleRunAsync(dbContext);
        var (_, _, reviewAttempt, _) = SeedChallengedReview(dbContext, run.Id, workspace.Id, staleCheckpoint.Id, Fingerprint, Now);

        var currentFingerprint = new string('c', 64);
        var currentCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 2, Now.AddMinutes(1), new string('c', 40), currentFingerprint, []);
        dbContext.GitCheckpoints.Add(currentCheckpoint);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(currentFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, reviewAttempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.challenged_review_checkpoint_stale", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_a_challenge_does_not_reply_to_the_original_proposal()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var (planningAttempt, _, reviewAttempt, _) = SeedChallengedReview(dbContext, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now, challengeCount: 1);

        var unrelatedProposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
            "An unrelated proposal.",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "Unrelated",
                implementationSteps = "Steps",
                risks = "Risks",
                verificationPlan = "Plan",
                escalationPoints = "None",
            }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.CollaborationMessages.Add(unrelatedProposal);

        // A second challenge on the same review attempt, but replying to the unrelated proposal
        // instead of the one this review actually reviewed.
        dbContext.CollaborationMessages.Add(CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, reviewAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Claude, ParticipantKind.Codex, CollaborationMessageType.Challenge, unrelatedProposal.Id,
            "Misdirected challenge",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                disputedItem = "x",
                materialImpact = "y",
                reasoning = "z",
                alternativeOrQuestion = "w",
            }),
            CollaborationMessageProvenance.ProviderObserved, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, reviewAttempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.challenges_not_valid", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_this_challenged_review_already_has_a_successful_resolution()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var (_, _, reviewAttempt, challenges) = SeedChallengedReview(dbContext, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var priorResolution = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 3, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        priorResolution.MarkAgentDispatched(Now);
        priorResolution.CompleteAgent(AgentOutcome.Resolved, Fingerprint, Now);
        dbContext.Attempts.Add(priorResolution);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), priorResolution.Id, challenges[0].Id, sequence: 1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, reviewAttempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.already_resolved", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_and_creates_no_attempt_when_sealing_the_context_manifest_fails()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var (_, _, reviewAttempt, _) = SeedChallengedReview(dbContext, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint),
            new FakeArtifactStore { SealShouldFail = true }, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new CreateChallengeResolutionAttemptCommand(run.Id, reviewAttempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.context_manifest_seal_failed", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Attempts.Where(a => a.AgentRole == AgentRole.Resolver));
    }
}
