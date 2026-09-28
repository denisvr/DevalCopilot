using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetSealedAgentArtifactWindow;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Proves the sealed Agent-artifact window query resolves strictly through the collaboration
/// message's own durable <see cref="CollaborationMessage.AttemptId"/> foreign key — mirroring
/// <c>GetCollaborationMessageEvidenceQueryHandlerTests</c>' own resolution coverage — then adds
/// this operation's own artifact-lookup, purpose-allowlist, and sealed-read-status projection.
/// </summary>
public sealed class GetSealedAgentArtifactWindowQueryHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Add the table then the query\",\"risks\":\"Unbounded content\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None expected\"}";

    private sealed class FakeArtifactStore : IArtifactStore
    {
        public SealedReadWindow? SealedResponse { get; set; }

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => "unused";
        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => "unused";
        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken ct) =>
            Task.FromResult<SealedOutputFile?>(null);
        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;
        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;
        public Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken ct) =>
            Task.FromResult<SealedOutputFile?>(null);
        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) { }
        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) { }
        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken ct) =>
            Task.FromResult(new PartialReadWindow(string.Empty, fromOffset, 0));

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes, CancellationToken ct) =>
            Task.FromResult(SealedResponse ?? new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0));
    }

    private static Attempt ClaimDispatchedPlannerAttempt(Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, claimedAtUtc, attemptNumber);
        attempt.MarkAgentDispatched(claimedAtUtc.AddSeconds(1));
        return attempt;
    }

    private static CollaborationMessage RecordProposal(Attempt attempt, DateTimeOffset occurredAtUtc) =>
        CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Proposal, null, "A proposal", ProposalContent, occurredAtUtc);

    [Fact]
    public async Task HandleAsync_fails_with_a_safe_not_found_for_an_unknown_message()
    {
        await using var dbContext = fixture.CreateContext();
        var handler = new GetSealedAgentArtifactWindowQueryHandler(dbContext, new FakeArtifactStore());

        var result = await handler.HandleAsync(
            new GetSealedAgentArtifactWindowQuery(Guid.NewGuid(), Guid.NewGuid(), ArtifactPurpose.AgentFinalResponse, 0, 1024),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("collaboration_messages.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_with_a_safe_not_found_when_the_message_belongs_to_a_different_run()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var ownerRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Owner run", Now);
        var otherRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 2, "Other run", Now);
        ownerRun.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(ownerRun.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.AddRange(ownerRun, otherRun);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetSealedAgentArtifactWindowQueryHandler(dbContext, new FakeArtifactStore());
        var result = await handler.HandleAsync(
            new GetSealedAgentArtifactWindowQuery(otherRun.Id, message.Id, ArtifactPurpose.AgentFinalResponse, 0, 1024),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("collaboration_messages.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_returns_no_agent_evidence_for_a_non_provider_observed_message()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Simulated proposal", Now);
        var message = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "A simulated proposal", ProposalContent,
            CollaborationMessageProvenance.Simulated, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetSealedAgentArtifactWindowQueryHandler(dbContext, new FakeArtifactStore());
        var result = await handler.HandleAsync(
            new GetSealedAgentArtifactWindowQuery(run.Id, message.Id, ArtifactPurpose.AgentFinalResponse, 0, 1024),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SealedAgentArtifactWindowStatus.NoAgentEvidence, result.Value.Status);
        Assert.Equal(string.Empty, result.Value.Text);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_when_the_linked_attempts_role_does_not_coherently_match_the_messages_actor()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Incoherent role", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attempts SET AgentRole = {nameof(AgentRole.Implementer)} WHERE Id = {attempt.Id}");

        var handler = new GetSealedAgentArtifactWindowQueryHandler(dbContext, new FakeArtifactStore());
        var result = await handler.HandleAsync(
            new GetSealedAgentArtifactWindowQuery(run.Id, message.Id, ArtifactPurpose.AgentFinalResponse, 0, 1024),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SealedAgentArtifactWindowStatus.AttemptLinkBroken, result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_for_a_purpose_outside_the_agent_artifact_allowlist()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Disallowed purpose", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetSealedAgentArtifactWindowQueryHandler(dbContext, new FakeArtifactStore());
        var result = await handler.HandleAsync(
            // A Process-attempt purpose, not one of the four Agent-artifact purposes this
            // operation ever serves — never reachable via the endpoint's own route allowlist,
            // exercised here as the handler's own defense-in-depth check.
            new GetSealedAgentArtifactWindowQuery(run.Id, message.Id, ArtifactPurpose.ProcessStandardOutput, 0, 1024),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SealedAgentArtifactWindowStatus.PurposeNotAllowlisted, result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_reports_artifact_not_found_when_no_row_exists_for_the_requested_purpose()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "No artifact", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetSealedAgentArtifactWindowQueryHandler(dbContext, new FakeArtifactStore());
        var result = await handler.HandleAsync(
            new GetSealedAgentArtifactWindowQuery(run.Id, message.Id, ArtifactPurpose.AgentStandardOutput, 0, 1024),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SealedAgentArtifactWindowStatus.ArtifactNotFound, result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_returns_a_verified_window_for_a_recorded_artifact()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Verified window", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        var artifact = Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            @"runs\r\attempts\a\final.sealed", "sha256:final", 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        dbContext.Artifacts.Add(artifact);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var store = new FakeArtifactStore { SealedResponse = new SealedReadWindow(SealedReadStatus.Ok, "final response text", 128, 128) };
        var handler = new GetSealedAgentArtifactWindowQueryHandler(dbContext, store);
        var result = await handler.HandleAsync(
            new GetSealedAgentArtifactWindowQuery(run.Id, message.Id, ArtifactPurpose.AgentFinalResponse, 0, 1024),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SealedAgentArtifactWindowStatus.Ok, result.Value.Status);
        Assert.Equal("final response text", result.Value.Text);
        Assert.Equal(128, result.Value.NextOffset);
        Assert.Equal(128, result.Value.TotalLengthSoFar);
        Assert.False(result.Value.Truncated);
    }

    [Fact]
    public async Task HandleAsync_surfaces_an_integrity_mismatch_rather_than_serving_unverified_bytes()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Tampered artifact", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        var artifact = Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            @"runs\r\attempts\a\final.sealed", "sha256:final", 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        dbContext.Artifacts.Add(artifact);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var store = new FakeArtifactStore
        {
            SealedResponse = new SealedReadWindow(SealedReadStatus.IntegrityMismatch, string.Empty, 0, 999),
        };
        var handler = new GetSealedAgentArtifactWindowQueryHandler(dbContext, store);
        var result = await handler.HandleAsync(
            new GetSealedAgentArtifactWindowQuery(run.Id, message.Id, ArtifactPurpose.AgentFinalResponse, 0, 1024),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SealedAgentArtifactWindowStatus.IntegrityMismatch, result.Value.Status);
        Assert.Equal(string.Empty, result.Value.Text);
        Assert.Null(result.Value.Truncated);
    }

    [Fact]
    public async Task HandleAsync_reports_missing_when_the_recorded_artifacts_sealed_file_cannot_be_found()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Missing sealed file", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        var artifact = Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            @"runs\r\attempts\a\final.sealed", "sha256:final", 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        dbContext.Artifacts.Add(artifact);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetSealedAgentArtifactWindowQueryHandler(dbContext, new FakeArtifactStore());
        var result = await handler.HandleAsync(
            new GetSealedAgentArtifactWindowQuery(run.Id, message.Id, ArtifactPurpose.AgentFinalResponse, 0, 1024),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SealedAgentArtifactWindowStatus.Missing, result.Value.Status);
        Assert.Equal(string.Empty, result.Value.Text);
    }
}
