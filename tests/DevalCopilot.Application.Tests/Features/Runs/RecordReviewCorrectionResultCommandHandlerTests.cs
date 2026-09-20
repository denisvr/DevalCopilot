using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RecordReviewCorrectionResultCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly string StartingHead = new('a', 40);
    private static readonly string StartingFingerprint = new('b', 64);
    private static readonly string ResultFingerprint = new('c', 64);
    private static readonly string CorrectionFingerprint = new('d', 64);

    [Fact]
    public async Task HandleAsync_records_the_complete_correction_ledger_chain_atomically()
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext);
        var correction = ValidatedReviewCorrection.Create(
            [
                new ValidatedRevisionResponse(seed.FirstFinding.Id, "Applied", "Fixed the null path.", "Updated src/Foo.cs."),
                new ValidatedRevisionResponse(seed.SecondFinding.Id, "Applied", "Added the missing guard.", "Updated src/Bar.cs."),
            ],
            ValidatedImplementationReport.Create(
                "Correction completed.", ["src/Foo.cs"], "Applied the requested fixes.", string.Empty, string.Empty,
                "Run the focused tests."));

        var handler = new RecordReviewCorrectionResultCommandHandler(
            dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordReviewCorrectionResultCommand(
                seed.Run.Id, seed.CorrectionAttempt.Id, true, StartingHead, CorrectionFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [], correction, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? string.Join("; ", result.Errors.Select(error => error.Code)) : null);
        Assert.Equal(AgentOutcome.CorrectionApplied, result.Value.Outcome);
        Assert.Equal(AttemptStatus.Completed, seed.CorrectionAttempt.Status);
        var checkpoint = Assert.Single(dbContext.GitCheckpoints, item => item.Id == seed.CorrectionAttempt.AgentResultGitCheckpointId);
        Assert.Equal(CorrectionFingerprint, checkpoint.FingerprintSha256);

        var messages = dbContext.CollaborationMessages.Where(message => message.AttemptId == seed.CorrectionAttempt.Id).OrderBy(message => message.Sequence).ToList();
        Assert.Equal(3, messages.Count);
        var responses = messages.Where(message => message.Type == CollaborationMessageType.RevisionResponse).ToArray();
        Assert.Equal(2, responses.Length);
        Assert.Equal(seed.FirstFinding.Id, responses[0].InReplyToMessageId);
        Assert.Equal(seed.SecondFinding.Id, responses[1].InReplyToMessageId);
        var executionReport = Assert.Single(messages, message => message.Type == CollaborationMessageType.ExecutionReport);
        Assert.Equal(seed.Proposal.Id, executionReport.InReplyToMessageId);
        Assert.Equal(seed.Proposal.Actor, executionReport.Recipient);
        Assert.All(messages, message => Assert.NotEqual(message.Actor, message.Recipient));
        Assert.Equal(seed.CorrectionAttempt.AgentRole, executionReport.Actor.Role);

        var events = dbContext.Events.Where(item => item.AttemptId == seed.CorrectionAttempt.Id).OrderBy(item => item.Sequence).ToList();
        Assert.Equal(3, events.Count);
        Assert.All(events, item => Assert.Equal(RunEventType.CollaborationMessageRecorded, item.EventType));
        Assert.Equal(messages.Select(message => message.Actor), events.Select(item => item.Actor));
    }

    [Fact]
    public Task HandleAsync_preserves_provider_failure_and_flags_mutation_when_fingerprint_changed() =>
        AssertTerminalOutcomeAsync(false, StartingHead, CorrectionFingerprint, true, AgentOutcome.ProviderInvocationFailed, WorkspaceStatus.NeedsAttention);

    [Fact]
    public Task HandleAsync_preserves_provider_failure_and_flags_mutation_when_head_changed() =>
        AssertTerminalOutcomeAsync(false, new string('e', 40), CorrectionFingerprint, true, AgentOutcome.ProviderInvocationFailed, WorkspaceStatus.NeedsAttention);

    [Fact]
    public Task HandleAsync_records_invalid_output_and_flags_mutation_after_a_successful_process() =>
        AssertTerminalOutcomeAsync(true, StartingHead, CorrectionFingerprint, true, AgentOutcome.InvalidStructuredOutput, WorkspaceStatus.NeedsAttention);

    [Fact]
    public Task HandleAsync_records_unavailable_evidence_and_flags_the_workspace() =>
        AssertTerminalOutcomeAsync(true, null, null, false, AgentOutcome.CheckpointEvidenceUnavailable, WorkspaceStatus.NeedsAttention);

    [Fact]
    public Task HandleAsync_rejects_changed_fingerprint_without_observed_paths() =>
        AssertIncoherentEvidenceAsync(StartingHead, CorrectionFingerprint, false);

    [Fact]
    public Task HandleAsync_rejects_unchanged_fingerprint_with_observed_paths() =>
        AssertIncoherentEvidenceAsync(StartingHead, ResultFingerprint, true);

    [Fact]
    public Task HandleAsync_rejects_partial_completion_evidence() =>
        AssertIncoherentEvidenceAsync(null, ResultFingerprint, false);

    [Fact]
    public Task HandleAsync_rejects_unavailable_evidence_with_observed_paths() =>
        AssertIncoherentEvidenceAsync(null, null, true);

    [Fact]
    public async Task HandleAsync_rejects_a_hand_built_overlong_revision_summary_before_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext);
        var correction = ValidatedReviewCorrection.Create(
            [
                new ValidatedRevisionResponse(seed.FirstFinding.Id, new string('x', 601), "Evidence", "Changed it."),
                new ValidatedRevisionResponse(seed.SecondFinding.Id, "Applied", "Evidence", "Changed it."),
            ],
            ValidatedImplementationReport.Create(
                "Correction completed.", ["src/Foo.cs"], "Applied the requested fixes.", string.Empty, string.Empty,
                "Run the focused tests."));
        var handler = new RecordReviewCorrectionResultCommandHandler(
            dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordReviewCorrectionResultCommand(
                seed.Run.Id, seed.CorrectionAttempt.Id, true, StartingHead, CorrectionFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [], correction, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_correction_result", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, seed.CorrectionAttempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, dbContext.GitWorkspaces.Single(item => item.Id == seed.CorrectionAttempt.AgentGitWorkspaceId).Status);
        Assert.Null(seed.CorrectionAttempt.AgentResultGitCheckpointId);
    }

    private async Task AssertTerminalOutcomeAsync(
        bool processSucceeded,
        string? completionHead,
        string? completionFingerprint,
        bool includeObservedPath,
        AgentOutcome expectedOutcome,
        WorkspaceStatus expectedWorkspaceStatus)
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext);
        var handler = new RecordReviewCorrectionResultCommandHandler(
            dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordReviewCorrectionResultCommand(
                seed.Run.Id, seed.CorrectionAttempt.Id, processSucceeded, completionHead, completionFingerprint,
                includeObservedPath ? [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")] : [], [], null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? string.Join("; ", result.Errors.Select(error => error.Code)) : null);
        Assert.Equal(expectedOutcome, result.Value.Outcome);
        Assert.Equal(AttemptStatus.Failed, seed.CorrectionAttempt.Status);
        Assert.Equal(expectedWorkspaceStatus, dbContext.GitWorkspaces.Single(item => item.Id == seed.CorrectionAttempt.AgentGitWorkspaceId).Status);
        Assert.Null(seed.CorrectionAttempt.AgentResultGitCheckpointId);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.AttemptId == seed.CorrectionAttempt.Id));
    }

    private async Task AssertIncoherentEvidenceAsync(string? completionHead, string? completionFingerprint, bool includeObservedPath)
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext);
        var handler = new RecordReviewCorrectionResultCommandHandler(
            dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordReviewCorrectionResultCommand(
                seed.Run.Id, seed.CorrectionAttempt.Id, true, completionHead, completionFingerprint,
                includeObservedPath ? [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")] : [], [], null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.incoherent_completion_evidence", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, seed.CorrectionAttempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, dbContext.GitWorkspaces.Single(item => item.Id == seed.CorrectionAttempt.AgentGitWorkspaceId).Status);
        Assert.Empty(dbContext.GitCheckpoints.Where(item => item.Id == seed.CorrectionAttempt.AgentResultGitCheckpointId));
        Assert.Empty(dbContext.Events.Where(item => item.AttemptId == seed.CorrectionAttempt.Id));
    }

    private static async Task<Seed> SeedAsync(DevalCopilotDbContext dbContext)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the reviewed implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", StartingHead, "main", Now);
        workspace.MarkReady();
        var physicalIdentity = project.Id.ToByteArray();
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(physicalIdentity), physicalIdentity, Now);
        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), Now, StartingHead, StartingFingerprint, []);
        var implementationCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), Now, StartingHead, ResultFingerprint, []);

        var planner = Attempt.ClaimAgent(Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, StartingFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        planner.MarkAgentDispatched(Now);
        planner.CompleteAgent(AgentOutcome.Proposed, StartingFingerprint, Now);
        var proposal = CollaborationMessage.RecordAgent(
            planner, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "Implement the requested change.", JsonSerializer.Serialize(new
            {
                scope = "Correction",
                implementationSteps = "Update the implementation.",
                risks = "Regression risk.",
                verificationPlan = "Run tests.",
                escalationPoints = "None.",
            }), Now);

        var implementation = Attempt.ClaimAgentImplementation(Guid.NewGuid(), run.Id, 2, workspace.Id, startingCheckpoint.Id, StartingFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now);
        implementation.MarkAgentDispatched(Now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, implementationCheckpoint.Id, Now);
        var executionReport = CollaborationMessage.RecordAgent(
            implementation, Guid.NewGuid(), proposal.Actor, CollaborationMessageType.ExecutionReport, proposal.Id,
            "Implemented the requested change.", JsonSerializer.Serialize(new { completedWork = "Updated the implementation.", verification = "Run tests." }), Now);

        var review = Attempt.ClaimAgentCodeReview(Guid.NewGuid(), run.Id, 3, workspace.Id, implementationCheckpoint.Id, ResultFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        review.MarkAgentDispatched(Now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, ResultFingerprint, Now);
        var firstFinding = CollaborationMessage.RecordAgent(
            review, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.ReviewFinding,
            executionReport.Id, "Fix the null path.", JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "Null path", requiredChange = "Add guard" }), Now);
        var secondFinding = CollaborationMessage.RecordAgent(
            review, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.ReviewFinding,
            executionReport.Id, "Add the missing guard.", JsonSerializer.Serialize(new { severity = "medium", category = "correctness", evidence = "Missing guard", requiredChange = "Add guard" }), Now);

        var correctionAttempt = Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), run.Id, 4, workspace.Id, implementationCheckpoint.Id, ResultFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now);
        correctionAttempt.MarkAgentDispatched(Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.GitCheckpoints.AddRange(startingCheckpoint, implementationCheckpoint);
        dbContext.Attempts.AddRange(planner, implementation, review, correctionAttempt);
        dbContext.CollaborationMessages.AddRange(proposal, executionReport, firstFinding, secondFinding);
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), review.Id, executionReport.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), review.Id, firstFinding.Id, 1),
            AttemptInputMessage.Record(Guid.NewGuid(), review.Id, secondFinding.Id, 2),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), correctionAttempt.Id, executionReport.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), correctionAttempt.Id, firstFinding.Id, 1),
            AttemptInputMessage.Record(Guid.NewGuid(), correctionAttempt.Id, secondFinding.Id, 2));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return new Seed(run, proposal, firstFinding, secondFinding, correctionAttempt, startingCheckpoint.Id);
    }

    private sealed record Seed(
        Run Run,
        CollaborationMessage Proposal,
        CollaborationMessage FirstFinding,
        CollaborationMessage SecondFinding,
        Attempt CorrectionAttempt,
        Guid StartingCheckpointId);
}
