using System.Security.Cryptography;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Mirrors <c>CreateChallengeResolutionAttemptCommandHandlerTests</c>'s fixture/fake style
/// exactly, adapted for the closed ExecutionReport/result-checkpoint/verification-evidence
/// eligibility chain this handler alone has.</summary>
public sealed class CreateCodeReviewAttemptCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private const string DotnetExecutablePath = @"C:\dotnet.exe";
    private const string NpmExecutablePath = @"C:\npm.cmd";

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

        private static readonly string PartialRoot = Path.Combine(Path.GetTempPath(), "devalcopilot-app-tests-code-review-partials");

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

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose)
        {
        }

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(new PartialReadWindow(string.Empty, fromOffset, 0));

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0));
    }

    private async Task<(Run Run, GitWorkspace Workspace)> SeedEligibleRunAsync(
        DevalCopilotDbContext dbContext, bool workspaceReady = true, bool leaseActive = true, bool codexObserved = true)
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

        return (run, workspace);
    }

    /// <summary>Seeds a complete, valid successful implementation chain: a planning attempt and
    /// its resolved-plan Proposal, a completed Implementer attempt whose real result checkpoint is
    /// <paramref name="resultCheckpoint"/>, and its one provider-observed ExecutionReport replying
    /// to the resolved plan — the exact evidence <c>CreateCodeReviewAttemptCommandHandler</c>
    /// requires.</summary>
    private static (CollaborationMessage ResolvedPlan, Attempt ImplementerAttempt, CollaborationMessage ExecutionReport) SeedImplementedExecution(
        DevalCopilotDbContext dbContext,
        Guid runId,
        Guid workspaceId,
        Guid startingCheckpointId,
        Guid resultCheckpointId,
        string startingCheckpointFingerprintSha256,
        DateTimeOffset occurredAtUtc)
    {
        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, workspaceId, startingCheckpointId, startingCheckpointFingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, occurredAtUtc);
        planningAttempt.MarkAgentDispatched(occurredAtUtc);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, startingCheckpointFingerprintSha256, occurredAtUtc);
        dbContext.Attempts.Add(planningAttempt);

        var resolvedPlan = CollaborationMessage.Record(
            Guid.NewGuid(), runId, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
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
        dbContext.CollaborationMessages.Add(resolvedPlan);

        var implementerAttempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), runId, 2, workspaceId, startingCheckpointId, startingCheckpointFingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, occurredAtUtc);
        implementerAttempt.MarkAgentDispatched(occurredAtUtc);
        implementerAttempt.CompleteImplementation(AgentOutcome.Implemented, resultCheckpointId, occurredAtUtc);
        dbContext.Attempts.Add(implementerAttempt);

        var executionReport = CollaborationMessage.Record(
            Guid.NewGuid(), runId, implementerAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Claude, ParticipantKind.Codex, CollaborationMessageType.ExecutionReport, resolvedPlan.Id,
            "Added the ledger table and its query.",
            JsonSerializer.Serialize(new { completedWork = "Added table and query.", verification = "dotnet test" }),
            CollaborationMessageProvenance.ProviderObserved, occurredAtUtc);
        dbContext.CollaborationMessages.Add(executionReport);

        return (resolvedPlan, implementerAttempt, executionReport);
    }

    private static VerificationExecution SeedPassedVerificationExecution(
        DevalCopilotDbContext dbContext,
        Guid projectId,
        Guid workspaceId,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        VerificationCommand command,
        int executionNumber,
        DateTimeOffset occurredAtUtc,
        VerificationExecutionStatus status = VerificationExecutionStatus.Passed)
    {
        var execution = VerificationExecution.Claim(Guid.NewGuid(), projectId, executionNumber, workspace, checkpoint, command, occurredAtUtc);
        execution.MarkDispatched(occurredAtUtc);
        if (status == VerificationExecutionStatus.Passed)
        {
            execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, occurredAtUtc);
        }
        else if (status == VerificationExecutionStatus.Failed)
        {
            execution.Complete(VerificationExecutionOutcome.Exited, 1, checkpoint.FingerprintSha256, occurredAtUtc);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        dbContext.VerificationExecutions.Add(execution);
        return execution;
    }

    [Fact]
    public async Task HandleAsync_creates_a_code_review_attempt_bound_to_the_ordered_verification_evidence_set()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace) = await SeedEligibleRunAsync(dbContext);

        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(startingCheckpoint);
        var resultFingerprint = new string('b', 64);
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), resultFingerprint, []);
        dbContext.GitCheckpoints.Add(resultCheckpoint);

        var (_, _, executionReport) = SeedImplementedExecution(
            dbContext, run.Id, workspace.Id, startingCheckpoint.Id, resultCheckpoint.Id, Fingerprint, Now);

        var commandOne = VerificationCommand.Configure(Guid.NewGuid(), run.ProjectId, 1, "Backend tests", DotnetExecutablePath, ["test"], 300, true, Now);
        var commandTwo = VerificationCommand.Configure(Guid.NewGuid(), run.ProjectId, 2, "Frontend tests", NpmExecutablePath, ["test"], 300, true, Now);
        dbContext.VerificationCommands.AddRange(commandOne, commandTwo);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var executionOne = SeedPassedVerificationExecution(dbContext, run.ProjectId, workspace.Id, workspace, resultCheckpoint, commandOne, 1, Now);
        var executionTwo = SeedPassedVerificationExecution(dbContext, run.ProjectId, workspace.Id, workspace, resultCheckpoint, commandTwo, 2, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(resultFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReport.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);

        var attempt = Assert.Single(dbContext.Attempts, a => a.Id == result.Value.AttemptId);
        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AgentProvider.Codex, attempt.AgentProvider);
        Assert.Equal(AgentRole.CodeReviewer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.ImplementationReview, attempt.AgentResponseContract);
        Assert.Equal(resultCheckpoint.Id, attempt.AgentGitCheckpointId);

        var inputMessage = Assert.Single(dbContext.AttemptInputMessages.Where(m => m.AttemptId == attempt.Id));
        Assert.Equal(0, inputMessage.Sequence);
        Assert.Equal(executionReport.Id, inputMessage.CollaborationMessageId);

        var orderedEvidence = dbContext.AttemptVerificationEvidence
            .Where(e => e.AttemptId == attempt.Id)
            .OrderBy(e => e.Sequence)
            .ToList();
        Assert.Equal(2, orderedEvidence.Count);
        Assert.Equal(executionOne.Id, orderedEvidence[0].VerificationExecutionId);
        Assert.Equal(executionTwo.Id, orderedEvidence[1].VerificationExecutionId);
    }

    // Discriminating regression for Slice B.1: production's ClaimAgent* factories always fix
    // Implementer to ClaudeCode, so this substitutes the implementer attempt's provider via
    // reflection (AttemptProviderSubstitution — a test-only helper, never a production path) and
    // constructs its ExecutionReport via RecordAgent (deriving Actor from the substituted
    // provider), purely to prove this handler's eligibility gate is authorized by AgentRole alone.
    // Before this correction, the gate compared the implementer attempt's AgentProvider (and the
    // ExecutionReport's Actor) to fixed ClaudeCode literals, which would have rejected this exact
    // scenario.
    [Fact]
    public async Task HandleAsync_accepts_an_execution_report_from_the_alternate_provider()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace) = await SeedEligibleRunAsync(dbContext);

        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(startingCheckpoint);
        var resultFingerprint = new string('b', 64);
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), resultFingerprint, []);
        dbContext.GitCheckpoints.Add(resultCheckpoint);

        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        planningAttempt.MarkAgentDispatched(Now);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now);
        dbContext.Attempts.Add(planningAttempt);

        var resolvedPlan = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
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
            CollaborationMessageProvenance.ProviderObserved, Now);
        dbContext.CollaborationMessages.Add(resolvedPlan);

        var implementerAttempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 2, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        implementerAttempt.MarkAgentDispatched(Now);
        implementerAttempt.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now);
        AttemptProviderSubstitution.SetProvider(implementerAttempt, AgentProvider.Codex);
        dbContext.Attempts.Add(implementerAttempt);

        var executionReport = CollaborationMessage.RecordAgent(
            implementerAttempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.ExecutionReport, resolvedPlan.Id,
            "Added the ledger table and its query.",
            JsonSerializer.Serialize(new { completedWork = "Added table and query.", verification = "dotnet test" }),
            Now);
        dbContext.CollaborationMessages.Add(executionReport);

        var commandOne = VerificationCommand.Configure(Guid.NewGuid(), run.ProjectId, 1, "Backend tests", DotnetExecutablePath, ["test"], 300, true, Now);
        dbContext.VerificationCommands.Add(commandOne);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        SeedPassedVerificationExecution(dbContext, run.ProjectId, workspace.Id, workspace, resultCheckpoint, commandOne, 1, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(resultFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReport.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_run_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_workspace_is_not_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, _) = await SeedEligibleRunAsync(dbContext, workspaceReady: false);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.workspace_not_ready", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_codex_capability_was_never_observed_successfully()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace) = await SeedEligibleRunAsync(dbContext, codexObserved: false);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.provider_not_observed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_execution_report_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace) = await SeedEligibleRunAsync(dbContext);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.execution_report_not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_result_checkpoint_is_not_the_workspaces_exact_current_checkpoint()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace) = await SeedEligibleRunAsync(dbContext);

        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(startingCheckpoint);
        var resultFingerprint = new string('b', 64);
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), resultFingerprint, []);
        dbContext.GitCheckpoints.Add(resultCheckpoint);

        var (_, _, executionReport) = SeedImplementedExecution(
            dbContext, run.Id, workspace.Id, startingCheckpoint.Id, resultCheckpoint.Id, Fingerprint, Now);

        // A THIRD checkpoint becomes current after the implementation — the review must never
        // treat a stale (even if real) result checkpoint as still reviewable.
        var laterFingerprint = new string('c', 64);
        var laterCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 3, Now, new string('c', 40), laterFingerprint, []);
        dbContext.GitCheckpoints.Add(laterCheckpoint);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(laterFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReport.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.result_checkpoint_mismatch", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_no_verification_command_is_enabled()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace) = await SeedEligibleRunAsync(dbContext);

        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(startingCheckpoint);
        var resultFingerprint = new string('b', 64);
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), resultFingerprint, []);
        dbContext.GitCheckpoints.Add(resultCheckpoint);

        var (_, _, executionReport) = SeedImplementedExecution(
            dbContext, run.Id, workspace.Id, startingCheckpoint.Id, resultCheckpoint.Id, Fingerprint, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(resultFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReport.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.no_verification_commands_enabled", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_an_enabled_command_has_no_execution_bound_to_the_current_checkpoint()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace) = await SeedEligibleRunAsync(dbContext);

        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(startingCheckpoint);
        var resultFingerprint = new string('b', 64);
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), resultFingerprint, []);
        dbContext.GitCheckpoints.Add(resultCheckpoint);

        var (_, _, executionReport) = SeedImplementedExecution(
            dbContext, run.Id, workspace.Id, startingCheckpoint.Id, resultCheckpoint.Id, Fingerprint, Now);

        var command = VerificationCommand.Configure(Guid.NewGuid(), run.ProjectId, 1, "Backend tests", DotnetExecutablePath, ["test"], 300, true, Now);
        dbContext.VerificationCommands.Add(command);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(resultFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReport.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.verification_evidence_missing", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_an_enabled_commands_latest_bound_execution_has_not_passed()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace) = await SeedEligibleRunAsync(dbContext);

        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(startingCheckpoint);
        var resultFingerprint = new string('b', 64);
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), resultFingerprint, []);
        dbContext.GitCheckpoints.Add(resultCheckpoint);

        var (_, _, executionReport) = SeedImplementedExecution(
            dbContext, run.Id, workspace.Id, startingCheckpoint.Id, resultCheckpoint.Id, Fingerprint, Now);

        var command = VerificationCommand.Configure(Guid.NewGuid(), run.ProjectId, 1, "Backend tests", DotnetExecutablePath, ["test"], 300, true, Now);
        dbContext.VerificationCommands.Add(command);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        SeedPassedVerificationExecution(
            dbContext, run.ProjectId, workspace.Id, workspace, resultCheckpoint, command, 1, Now, VerificationExecutionStatus.Failed);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(resultFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReport.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.verification_evidence_not_passed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_this_exact_input_identity_already_has_a_successful_review()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace) = await SeedEligibleRunAsync(dbContext);

        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(startingCheckpoint);
        var resultFingerprint = new string('b', 64);
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), resultFingerprint, []);
        dbContext.GitCheckpoints.Add(resultCheckpoint);

        var (_, _, executionReport) = SeedImplementedExecution(
            dbContext, run.Id, workspace.Id, startingCheckpoint.Id, resultCheckpoint.Id, Fingerprint, Now);

        var command = VerificationCommand.Configure(Guid.NewGuid(), run.ProjectId, 1, "Backend tests", DotnetExecutablePath, ["test"], 300, true, Now);
        dbContext.VerificationCommands.Add(command);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var execution = SeedPassedVerificationExecution(dbContext, run.ProjectId, workspace.Id, workspace, resultCheckpoint, command, 1, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var priorReview = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 3, workspace.Id, resultCheckpoint.Id, resultFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        priorReview.MarkAgentDispatched(Now);
        priorReview.CompleteAgent(AgentOutcome.ReviewApproved, resultFingerprint, Now);
        dbContext.Attempts.Add(priorReview);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), priorReview.Id, executionReport.Id, sequence: 0));
        dbContext.AttemptVerificationEvidence.Add(
            DevalCopilot.Domain.Features.Runs.AttemptVerificationEvidence.Record(Guid.NewGuid(), priorReview.Id, command.Id, execution.Id, sequence: 0));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(resultFingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReport.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.already_code_reviewed", Assert.Single(result.Errors).Code);
    }
}
