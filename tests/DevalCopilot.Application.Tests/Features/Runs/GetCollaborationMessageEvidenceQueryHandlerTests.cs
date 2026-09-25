using DevalCopilot.Application.Features.Runs.Commands.RecordSimulatedAgentStep;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Proves the Increment 4 evidence drill-down resolves strictly through a collaboration message's
/// own durable <see cref="CollaborationMessage.AttemptId"/> foreign key — never a "latest attempt
/// of that role" substitute — and fails closed for every ambiguous or malformed shape: a
/// cross-run message id, a legitimate attemptless message, and a broken persisted attempt link.
/// </summary>
public sealed class GetCollaborationMessageEvidenceQueryHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Add the table then the query\",\"risks\":\"Unbounded content\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None expected\"}";

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

    /// <summary>Persists a minimal, real Planner attempt and its Proposal message so an
    /// ExecutionReport built for a checkpoint-fingerprint test has a valid reply parent — an
    /// ExecutionReport may only reply to a Proposal or Decision, never stand alone.</summary>
    private static Guid SeedProposalToReplyTo(DevalCopilotDbContext dbContext, Guid runId, Guid workspaceId, Guid startingCheckpointId, DateTimeOffset now)
    {
        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 100, workspaceId, startingCheckpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, 100);
        planningAttempt.MarkAgentDispatched(now);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        var proposal = RecordProposal(planningAttempt, now.AddSeconds(1));
        dbContext.Attempts.Add(planningAttempt);
        dbContext.CollaborationMessages.Add(proposal);
        return proposal.Id;
    }

    [Fact]
    public async Task HandleAsync_fails_with_a_safe_not_found_for_an_unknown_message()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

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

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        // The message id is real, but requested under a run that does not own it.
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(otherRun.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("collaboration_messages.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_returns_an_explicit_no_agent_evidence_result_for_an_attemptless_message()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Simulated proposal", Now);
        // A Simulated message legitimately has no linked Attempt — mirrors the Simulated-run
        // seeding pattern used by CollaborationTimelineEndpointTests.
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

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.NoAgentEvidence, result.Value.Status);
        Assert.Null(result.Value.AttemptId);
        Assert.Empty(result.Value.Artifacts);
    }

    [Fact]
    public async Task HandleAsync_returns_no_agent_evidence_for_a_message_recorded_through_the_real_simulated_step_path()
    {
        // Uses the real RecordSimulatedAgentStepCommandHandler — not a hand-built attemptless
        // synthetic message — to prove that a message which legitimately carries a non-null
        // AttemptId (linked to a real, persisted Simulated attempt) still resolves to
        // NoAgentEvidence: Provenance, not AttemptId nullability, is what this handler trusts.
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Simulated step evidence", Now);
        run.Claim(Now);
        var simulatedAttempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(simulatedAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var stepHandler = new RecordSimulatedAgentStepCommandHandler(dbContext, new FixedTimeProvider(Now));
        var stepResult = await stepHandler.HandleAsync(
            new RecordSimulatedAgentStepCommand(
                run.Id,
                simulatedAttempt.Id,
                RunStage.Plan,
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                RunEventType.CodexProposal,
                CollaborationMessageType.Proposal,
                null,
                "Simulated proposal",
                ProposalContent),
            CancellationToken.None);
        Assert.True(stepResult.IsSuccess);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, stepResult.Value.MessageId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.NoAgentEvidence, result.Value.Status);
        Assert.Null(result.Value.AttemptId);
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

        // Corrupts only the persisted attempt row's own role, leaving the message's own
        // ActorAgentRole (Planner, captured at construction) untouched — an incoherent
        // ownership no Domain factory can itself produce, but the read side must still catch.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attempts SET AgentRole = {nameof(AgentRole.Implementer)} WHERE Id = {attempt.Id}");

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.AttemptLinkBroken, result.Value.Status);
        Assert.Null(result.Value.AttemptId);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_when_the_linked_row_is_not_an_agent_kind_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Incoherent kind", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Corrupts the persisted row to a Simulated attempt while the ProviderObserved message
        // still points at its Id — a corrupted link must never be treated as real Agent evidence.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attempts SET Kind = {nameof(AttemptKind.Simulated)} WHERE Id = {attempt.Id}");

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.AttemptLinkBroken, result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_when_the_message_is_provider_observed_but_carries_no_attempt_id()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Incoherent provenance", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Only CollaborationMessage.RecordAgent can construct a ProviderObserved message, and it
        // always sets a real AttemptId — reproduces the otherwise-impossible shape directly.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE collaboration_messages SET AttemptId = NULL WHERE Id = {message.Id}");

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.AttemptLinkBroken, result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_when_the_linked_attempt_row_cannot_be_found()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Broken link", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        var message = RecordProposal(attempt, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Simulates an impossible-under-the-FK-constraint broken link: turn off enforcement, then
        // delete the attempt row while leaving the message's own AttemptId pointing at it.
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM attempts WHERE Id = {attempt.Id}");

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.AttemptLinkBroken, result.Value.Status);
        Assert.Null(result.Value.AttemptId);
        Assert.Empty(result.Value.Artifacts);
    }

    [Fact]
    public async Task HandleAsync_returns_the_exact_attempt_the_message_is_linked_to_even_when_another_same_role_attempt_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Multiple planner attempts", Now);
        run.Claim(Now);

        // A first Planner attempt that failed before dispatch — never linked to any message.
        var firstAttempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        firstAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddSeconds(1));

        // The exact Planner attempt the message under test is linked to.
        var linkedAttempt = ClaimDispatchedPlannerAttempt(run.Id, 2, Now.AddSeconds(2));
        var linkedMessage = RecordProposal(linkedAttempt, Now.AddSeconds(3));

        // A THIRD, later Planner attempt of the same role — proves the handler never substitutes
        // "the latest attempt of this role" for the message's own real link.
        var laterAttempt = ClaimDispatchedPlannerAttempt(run.Id, 3, Now.AddSeconds(4));
        laterAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddSeconds(5));

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(firstAttempt, linkedAttempt, laterAttempt);
        dbContext.CollaborationMessages.Add(linkedMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, linkedMessage.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.HasEvidence, result.Value.Status);
        Assert.Equal(linkedAttempt.Id, result.Value.AttemptId);
        Assert.Equal(2, result.Value.AttemptNumber);
        Assert.NotEqual(firstAttempt.Id, result.Value.AttemptId);
        Assert.NotEqual(laterAttempt.Id, result.Value.AttemptId);
    }

    [Fact]
    public async Task HandleAsync_projects_bounded_evidence_for_the_linked_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Evidence projection", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(2), TestProcessEvidence.CleanExit);
        var message = RecordProposal(attempt, Now.AddSeconds(3));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            @"runs\r\attempts\a\final.sealed", "sha256:final", 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value;
        Assert.Equal(CollaborationMessageEvidenceStatus.HasEvidence, value.Status);
        Assert.Equal(attempt.Id, value.AttemptId);
        Assert.Equal(1, value.AttemptNumber);
        Assert.Equal(AttemptKind.Agent, value.AttemptKind);
        Assert.Equal(AttemptStatus.Completed, value.AttemptStatus);
        Assert.Equal(AgentProvider.Codex, value.AgentProvider);
        Assert.Equal(AgentRole.Planner, value.AgentRole);
        Assert.Equal(AgentResponseContract.Proposal, value.AgentResponseContract);
        Assert.Equal(AgentOutcome.Proposed, value.AgentOutcome);
        Assert.Equal(attempt.AgentGitCheckpointId, value.StartingGitCheckpointId);
        Assert.Equal(Fingerprint, value.StartingCheckpointFingerprintSha256);
        Assert.Null(value.ResultGitCheckpointId);
        Assert.Equal(TestProcessEvidence.CleanExit, value.ProcessExecution);
        var artifact = Assert.Single(value.Artifacts);
        Assert.Equal(ArtifactPurpose.AgentFinalResponse, artifact.Purpose);
        Assert.False(value.ArtifactsOmitted);
        Assert.Equal(1, value.ArtifactTotalCount);
    }

    [Fact]
    public async Task HandleAsync_excludes_an_artifact_whose_run_id_does_not_match_the_requested_run_even_when_its_attempt_id_matches()
    {
        // Artifact.RunId is persisted independently of Artifact.AttemptId, and the database
        // enforces no composite (RunId, AttemptId) ownership constraint between them — only a
        // unique (AttemptId, Purpose) index exists. This reproduces a real, directly persisted
        // inconsistency: a row whose AttemptId genuinely matches this message's own linked
        // attempt, but whose RunId belongs to a different run entirely.
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Cross-run artifact", Now);
        var otherRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 2, "A different run", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(2), TestProcessEvidence.CleanExit);
        var message = RecordProposal(attempt, Now.AddSeconds(3));
        dbContext.Projects.Add(project);
        dbContext.Runs.AddRange(run, otherRun);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        // The one legitimate artifact: both RunId and AttemptId correctly belong to this run
        // and this attempt.
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            @"runs\r\attempts\a\final.sealed", "sha256:final", 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        // The inconsistent row: this attempt's own real Id, but otherRun's Id — a different
        // Purpose is required only to satisfy the unique (AttemptId, Purpose) index, not because
        // Purpose is otherwise relevant to this test.
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), otherRun.Id, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
            @"runs\r\attempts\a\context.sealed", "sha256:context", 64, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now.AddSeconds(1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var artifact = Assert.Single(result.Value.Artifacts);
        Assert.Equal(ArtifactPurpose.AgentFinalResponse, artifact.Purpose);
        Assert.False(result.Value.ArtifactsOmitted);
        Assert.Equal(1, result.Value.ArtifactTotalCount);
    }

    [Fact]
    public async Task HandleAsync_caps_the_returned_artifacts_and_reports_the_real_total_when_more_exist()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Many artifacts", Now);
        run.Claim(Now);
        var attempt = ClaimDispatchedPlannerAttempt(run.Id, 1, Now);
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now.AddSeconds(2), TestProcessEvidence.CleanExit);
        var message = RecordProposal(attempt, Now.AddSeconds(3));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        // The unique (AttemptId, Purpose) index means one attempt can hold at most one artifact
        // per ArtifactPurpose member — every currently defined purpose is seeded here (6 total)
        // to exercise the cap (5) with real, distinct-purpose persisted rows rather than a
        // hypothetical count this schema cannot actually produce.
        var purposes = Enum.GetValues<ArtifactPurpose>();
        for (var i = 0; i < purposes.Length; i++)
        {
            dbContext.Artifacts.Add(Artifact.Record(
                Guid.NewGuid(), run.Id, attempt.Id, purposes[i],
                "text/plain", $@"runs\r\attempts\a\{i}.sealed", $"sha256:{i}", 10, false,
                ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted,
                Now.AddSeconds(i)));
        }
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Artifacts.Count);
        Assert.True(result.Value.ArtifactsOmitted);
        Assert.Equal(purposes.Length, result.Value.ArtifactTotalCount);
    }

    [Fact]
    public async Task HandleAsync_includes_the_result_checkpoint_fingerprint_and_the_configured_timeout_when_coherent()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Result checkpoint fingerprint", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var resultFingerprint = new string('b', 64);
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), resultFingerprint, []);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.AddRange(startingCheckpoint, resultCheckpoint);
        var proposalId = SeedProposalToReplyTo(dbContext, run.Id, workspace.Id, startingCheckpoint.Id, Now);

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(15), 262144, 524288, Now, 1);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);
        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.ExecutionReport, proposalId, "Implemented the change.",
            "{\"completedWork\":\"Implemented the change.\",\"verification\":\"dotnet test — all green.\"}",
            Now.AddSeconds(3));

        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.HasEvidence, result.Value.Status);
        Assert.Equal(resultCheckpoint.Id, result.Value.ResultGitCheckpointId);
        Assert.Equal(resultFingerprint, result.Value.ResultCheckpointFingerprintSha256);
        Assert.Equal(TimeSpan.FromMinutes(15), result.Value.AgentTimeout);
    }

    [Fact]
    public async Task HandleAsync_omits_the_result_checkpoint_fingerprint_when_the_checkpoint_belongs_to_a_different_workspace()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Cross-workspace checkpoint", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        // A second, unrelated workspace whose own checkpoint will be (incoherently) referenced.
        var otherWorkspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        otherWorkspace.MarkReady();
        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var wrongWorkspaceCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), otherWorkspace.Id, 1, Now, new string('c', 40), new string('c', 64), []);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.AddRange(workspace, otherWorkspace);
        dbContext.GitCheckpoints.AddRange(startingCheckpoint, wrongWorkspaceCheckpoint);
        var proposalId = SeedProposalToReplyTo(dbContext, run.Id, workspace.Id, startingCheckpoint.Id, Now);

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteImplementation(AgentOutcome.Implemented, wrongWorkspaceCheckpoint.Id, Now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);
        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.ExecutionReport, proposalId, "Implemented the change.",
            "{\"completedWork\":\"Implemented the change.\",\"verification\":\"dotnet test — all green.\"}",
            Now.AddSeconds(3));

        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.HasEvidence, result.Value.Status);
        // The checkpoint id itself is still reported (it is what the attempt actually recorded),
        // but its fingerprint fails safely to null rather than ever substituting a fingerprint
        // that belongs to a different workspace's checkpoint.
        Assert.Equal(wrongWorkspaceCheckpoint.Id, result.Value.ResultGitCheckpointId);
        Assert.Null(result.Value.ResultCheckpointFingerprintSha256);
    }

    [Fact]
    public async Task HandleAsync_omits_the_result_checkpoint_fingerprint_when_the_referenced_checkpoint_row_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Missing result checkpoint", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('b', 40), new string('b', 64), []);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.AddRange(startingCheckpoint, resultCheckpoint);
        var proposalId = SeedProposalToReplyTo(dbContext, run.Id, workspace.Id, startingCheckpoint.Id, Now);

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);
        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.ExecutionReport, proposalId, "Implemented the change.",
            "{\"completedWork\":\"Implemented the change.\",\"verification\":\"dotnet test — all green.\"}",
            Now.AddSeconds(3));

        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Deletes the result checkpoint row itself while the attempt's own reference remains.
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM git_checkpoints WHERE Id = {resultCheckpoint.Id}");

        var handler = new GetCollaborationMessageEvidenceQueryHandler(dbContext);
        var result = await handler.HandleAsync(
            new GetCollaborationMessageEvidenceQuery(run.Id, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationMessageEvidenceStatus.HasEvidence, result.Value.Status);
        Assert.Equal(resultCheckpoint.Id, result.Value.ResultGitCheckpointId);
        Assert.Null(result.Value.ResultCheckpointFingerprintSha256);
    }

    [Fact]
    public void The_query_result_never_structurally_carries_any_excluded_evidence_field()
    {
        var excludedNames = new[]
        {
            "ProcessExecutablePath", "ProcessArguments", "ProcessWorkingDirectory", "ProcessApprovedRoot",
            "AgentProviderSessionId", "AgentAdapterContractVersion", "RelativeStoragePath", "ContentHash",
        };
        var propertyNames = typeof(CollaborationMessageEvidenceQueryResult).GetProperties().Select(p => p.Name).ToHashSet();

        foreach (var excluded in excludedNames)
        {
            Assert.DoesNotContain(excluded, propertyNames);
        }
    }
}
