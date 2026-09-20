using System.Security.Cryptography;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Mirrors <c>CreateChallengeResolutionAttemptCommandHandlerTests</c>'s fixture/fake
/// style exactly, adapted for the two eligible resolved-plan forms this handler alone accepts
/// (an accepted original Proposal, or a resolved revised Proposal).</summary>
public sealed class CreateImplementationAttemptCommandHandlerTests : IAsyncLifetime
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

        private static readonly string PartialRoot = Path.Combine(Path.GetTempPath(), "devalcopilot-app-tests-implementation-partials");

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
        DevalCopilotDbContext dbContext, bool claudeObserved = true)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Implement the resolved plan", Now);
        run.Claim(Now);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);

        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(checkpoint);

        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), Now);
        dbContext.RepositoryMutationLeases.Add(lease);

        var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        if (claudeObserved)
        {
            claude.MarkDispatched(Now);
            claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "2.1.276", Now, Now.AddMinutes(5));
        }

        dbContext.HostCapabilitySnapshots.Add(claude);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (project, run, workspace, await dbContext.GitCheckpoints.SingleAsync(c => c.WorkspaceId == workspace.Id));
    }

    private static CollaborationMessage RecordProposal(DevalCopilotDbContext dbContext, Guid runId, Guid attemptId, DateTimeOffset occurredAtUtc)
    {
        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), runId, attemptId, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
            "Add the ledger table and its query.",
            JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table then the query",
                risks = "Unbounded content",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }),
            CollaborationMessageProvenance.ProviderObserved, occurredAtUtc);
        dbContext.CollaborationMessages.Add(proposal);
        return proposal;
    }

    /// <summary>Seeds Case A: a completed Codex planning attempt with its Proposal, plus a
    /// completed, Accepted Claude critical-review attempt bound to the given workspace/checkpoint
    /// whose Acceptance replies to that Proposal.</summary>
    private static CollaborationMessage SeedAcceptedOriginalProposal(
        DevalCopilotDbContext dbContext, Guid runId, Guid workspaceId, Guid checkpointId, string fingerprint, DateTimeOffset occurredAtUtc)
    {
        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, occurredAtUtc);
        planningAttempt.MarkAgentDispatched(occurredAtUtc);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, fingerprint, occurredAtUtc);
        dbContext.Attempts.Add(planningAttempt);

        var proposal = RecordProposal(dbContext, runId, planningAttempt.Id, occurredAtUtc);

        var reviewAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, 2, workspaceId, checkpointId, fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, occurredAtUtc);
        reviewAttempt.MarkAgentDispatched(occurredAtUtc);
        reviewAttempt.CompleteAgent(AgentOutcome.Accepted, fingerprint, occurredAtUtc);
        dbContext.Attempts.Add(reviewAttempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), reviewAttempt.Id, proposal.Id, sequence: 0));

        var acceptance = CollaborationMessage.Record(
            Guid.NewGuid(), runId, reviewAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Claude, ParticipantKind.Codex, CollaborationMessageType.Acceptance, proposal.Id,
            "Accepted as proposed.",
            JsonSerializer.Serialize(new { rationale = "Sound and complete." }),
            CollaborationMessageProvenance.ProviderObserved, occurredAtUtc);
        dbContext.CollaborationMessages.Add(acceptance);

        return proposal;
    }

    /// <summary>Seeds Case B: a completed, Resolved Codex resolver attempt whose own recorded
    /// Proposal (the revised one) and ordered Decision set are its resolution evidence.</summary>
    private static CollaborationMessage SeedResolvedRevisedProposal(
        DevalCopilotDbContext dbContext, Guid runId, Guid workspaceId, Guid checkpointId, string fingerprint, DateTimeOffset occurredAtUtc)
    {
        var originalPlanningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, occurredAtUtc);
        originalPlanningAttempt.MarkAgentDispatched(occurredAtUtc);
        originalPlanningAttempt.CompleteAgent(AgentOutcome.Proposed, fingerprint, occurredAtUtc);
        dbContext.Attempts.Add(originalPlanningAttempt);
        var originalProposal = RecordProposal(dbContext, runId, originalPlanningAttempt.Id, occurredAtUtc);

        var challenge = CollaborationMessage.Record(
            Guid.NewGuid(), runId, originalPlanningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Claude, ParticipantKind.Codex, CollaborationMessageType.Challenge, originalProposal.Id,
            "The risk section is too thin.",
            JsonSerializer.Serialize(new
            {
                disputedItem = "Risks",
                materialImpact = "Could hide a real regression",
                reasoning = "No mitigation listed",
                alternativeOrQuestion = "Add a mitigation",
            }),
            CollaborationMessageProvenance.ProviderObserved, occurredAtUtc);
        dbContext.CollaborationMessages.Add(challenge);

        var resolverAttempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, 2, workspaceId, checkpointId, fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, occurredAtUtc);
        resolverAttempt.MarkAgentDispatched(occurredAtUtc);
        resolverAttempt.CompleteAgent(AgentOutcome.Resolved, fingerprint, occurredAtUtc);
        dbContext.Attempts.Add(resolverAttempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, originalProposal.Id, sequence: 0));
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, challenge.Id, sequence: 1));

        var decision = CollaborationMessage.Record(
            Guid.NewGuid(), runId, resolverAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Decision, challenge.Id,
            "Accepted; added a mitigation.",
            JsonSerializer.Serialize(new
            {
                resolution = "accepted",
                rationale = "Valid concern",
                resultingPlanChanges = "Added rollback step",
                nextAction = "None",
            }),
            CollaborationMessageProvenance.ProviderObserved, occurredAtUtc);
        dbContext.CollaborationMessages.Add(decision);

        var revisedProposal = CollaborationMessage.Record(
            Guid.NewGuid(), runId, resolverAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, originalProposal.Id,
            "Add the ledger table, its query, and a rollback step.",
            JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table, the query, and a rollback step",
                risks = "Unbounded content, mitigated by rollback",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }),
            CollaborationMessageProvenance.ProviderObserved, occurredAtUtc);
        dbContext.CollaborationMessages.Add(revisedProposal);

        return revisedProposal;
    }

    [Fact]
    public async Task HandleAsync_creates_an_implementation_attempt_for_an_accepted_original_proposal()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var proposal = SeedAcceptedOriginalProposal(dbContext, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateImplementationAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(run.Id, proposal.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var attempt = Assert.Single(dbContext.Attempts, a => a.Id == result.Value.AttemptId);
        Assert.Equal(AgentProvider.ClaudeCode, attempt.AgentProvider);
        Assert.Equal(AgentRole.Implementer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.ImplementationReport, attempt.AgentResponseContract);

        var orderedInputMessages = dbContext.AttemptInputMessages.Where(m => m.AttemptId == attempt.Id).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(2, orderedInputMessages.Count);
        Assert.Equal(proposal.Id, orderedInputMessages[0].CollaborationMessageId);
    }

    [Fact]
    public async Task HandleAsync_creates_an_implementation_attempt_for_a_resolved_revised_proposal()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var revisedProposal = SeedResolvedRevisedProposal(dbContext, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateImplementationAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(run.Id, revisedProposal.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var attempt = Assert.Single(dbContext.Attempts, a => a.Id == result.Value.AttemptId);

        var orderedInputMessages = dbContext.AttemptInputMessages.Where(m => m.AttemptId == attempt.Id).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(2, orderedInputMessages.Count);
        Assert.Equal(revisedProposal.Id, orderedInputMessages[0].CollaborationMessageId);
    }

    // Discriminating regression for Slice B.1: production's ClaimAgent* factories always fix
    // Resolver to Codex, so this substitutes the resolver attempt's provider via reflection
    // (AttemptProviderSubstitution — a test-only helper, never a production path) and constructs
    // its Decision/revised-Proposal messages with the matching alternate Actor, purely to prove
    // this handler's eligibility gate is authorized by AgentRole alone. Before this correction, the
    // gate compared the resolver attempt's AgentProvider (and each Decision's Actor) to fixed Codex
    // literals, which would have rejected this exact scenario.
    [Fact]
    public async Task HandleAsync_creates_an_implementation_attempt_for_a_resolved_revised_proposal_from_the_alternate_provider()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);

        var originalPlanningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        originalPlanningAttempt.MarkAgentDispatched(Now);
        originalPlanningAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now);
        dbContext.Attempts.Add(originalPlanningAttempt);
        var originalProposal = RecordProposal(dbContext, run.Id, originalPlanningAttempt.Id, Now);

        var challenge = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, originalPlanningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Claude, ParticipantKind.Codex, CollaborationMessageType.Challenge, originalProposal.Id,
            "The risk section is too thin.",
            JsonSerializer.Serialize(new
            {
                disputedItem = "Risks",
                materialImpact = "Could hide a real regression",
                reasoning = "No mitigation listed",
                alternativeOrQuestion = "Add a mitigation",
            }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.CollaborationMessages.Add(challenge);

        var resolverAttempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        resolverAttempt.MarkAgentDispatched(Now);
        resolverAttempt.CompleteAgent(AgentOutcome.Resolved, Fingerprint, Now);
        AttemptProviderSubstitution.SetProvider(resolverAttempt, AgentProvider.ClaudeCode);
        dbContext.Attempts.Add(resolverAttempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, originalProposal.Id, sequence: 0));
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, challenge.Id, sequence: 1));

        // RecordAgent, not the legacy Record: a Decision's legacy ValidateActorForType policy
        // (Codex or Human only) is irrelevant to a real Agent-authored message, which RecordAgent
        // never checks against — exactly as production behaves post-Slice-B.
        var decision = CollaborationMessage.RecordAgent(
            resolverAttempt, Guid.NewGuid(), ParticipantKind.Codex, CollaborationMessageType.Decision, challenge.Id,
            "Accepted; added a mitigation.",
            JsonSerializer.Serialize(new
            {
                resolution = "accepted",
                rationale = "Valid concern",
                resultingPlanChanges = "Added rollback step",
                nextAction = "None",
            }),
            Now);
        dbContext.CollaborationMessages.Add(decision);

        var revisedProposal = CollaborationMessage.RecordAgent(
            resolverAttempt, Guid.NewGuid(), ParticipantKind.Codex, CollaborationMessageType.Proposal, originalProposal.Id,
            "Add the ledger table, its query, and a rollback step.",
            JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table, the query, and a rollback step",
                risks = "Unbounded content, mitigated by rollback",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }),
            Now);
        dbContext.CollaborationMessages.Add(revisedProposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateImplementationAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(run.Id, revisedProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_fails_when_an_original_proposal_has_no_accepted_review_yet()
    {
        // The owning attempt is a completed Codex Planner/Proposed attempt — this routes into
        // the accepted-original-proposal validation branch, which correctly rejects it since no
        // Claude Accepted review exists for it yet. This is distinct from
        // agent_attempts.plan_not_resolved, which is reserved for a plan whose owning attempt is
        // neither a completed Planner/Proposed attempt nor a completed Resolver/Resolved attempt
        // at all (e.g. still Running, or a different role/outcome entirely).
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);

        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        planningAttempt.MarkAgentDispatched(Now);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);
        var proposal = RecordProposal(dbContext, run.Id, planningAttempt.Id, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateImplementationAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(run.Id, proposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.no_accepted_review_found", Assert.Single(result.Errors).Code);
    }

    // Fail-closed regression: an otherwise-valid Accepted critical-review attempt whose own
    // provider is missing or undefined (malformed persisted state) must be rejected safely and
    // without mutation — never allowed to reach AgentProviderParticipant.For and throw. The
    // corrupted review is simply invisible to this handler's own query (which requires
    // AgentProvider is not { } ... — see AgentAuthoredMessageEligibility's sibling reasoning),
    // so this surfaces as "no accepted review found," not a distinct error code.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_fails_and_does_not_mutate_when_the_accepted_reviews_own_provider_is_missing_or_undefined(bool useUndefinedRatherThanNull)
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);

        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        planningAttempt.MarkAgentDispatched(Now);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);
        var proposal = RecordProposal(dbContext, run.Id, planningAttempt.Id, Now);

        var reviewAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        reviewAttempt.MarkAgentDispatched(Now);
        reviewAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now);
        if (useUndefinedRatherThanNull)
        {
            AttemptProviderSubstitution.SetUndefinedProvider(reviewAttempt);
        }
        else
        {
            AttemptProviderSubstitution.SetProvider(reviewAttempt, (AgentProvider?)null);
        }

        dbContext.Attempts.Add(reviewAttempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), reviewAttempt.Id, proposal.Id, sequence: 0));
        var acceptance = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, reviewAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Claude, ParticipantKind.Codex, CollaborationMessageType.Acceptance, proposal.Id,
            "Accepted as proposed.",
            JsonSerializer.Serialize(new { rationale = "Sound and complete." }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.CollaborationMessages.Add(acceptance);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateImplementationAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(run.Id, proposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.no_accepted_review_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_plan_message_is_not_a_provider_observed_proposal()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);

        var handler = new CreateImplementationAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.not_provider_observed_plan_proposal", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_this_plan_already_has_a_successful_implementation()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var proposal = SeedAcceptedOriginalProposal(dbContext, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var priorImplementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 3, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        priorImplementation.MarkAgentDispatched(Now);
        priorImplementation.CompleteImplementation(AgentOutcome.Implemented, Guid.NewGuid(), Now);
        dbContext.Attempts.Add(priorImplementation);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), priorImplementation.Id, proposal.Id, sequence: 0));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateImplementationAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(run.Id, proposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.already_implemented", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_claude_capability_was_never_observed_successfully()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, claudeObserved: false);

        var handler = new CreateImplementationAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.provider_not_observed", Assert.Single(result.Errors).Code);
    }
}
