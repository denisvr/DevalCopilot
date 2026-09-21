using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class ImplementerExecutionReportEligibilitySequenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly string StartingFingerprint = new('a', 64);
    private static readonly string ResultFingerprint = new('b', 64);
    private static readonly string CorrectionFingerprint = new('c', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Valid_initial_chain_preserves_the_complete_sequence_order()
    {
        await using var context = _fixture.CreateContext();
        var seed = SeedInitialChain(context);
        await context.SaveChangesAsync(CancellationToken.None);

        await using var verificationContext = _fixture.CreateContext();
        var executionReport = await verificationContext.CollaborationMessages
            .SingleAsync(message => message.Id == seed.ExecutionReportId);
        var result = await ImplementerExecutionReportEligibility.ResolveAsync(
            verificationContext, executionReport, seed.RunId, seed.WorkspaceId, seed.ResultCheckpointId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(seed.ProposalId, result.OriginalProposal.Id);
    }

    [Theory]
    [InlineData(InitialSequenceCorruption.ExecutionReportBeforeProposal)]
    [InlineData(InitialSequenceCorruption.AcceptanceBeforeProposal)]
    public Task Malformed_initial_sequence_edges_are_ineligible_without_mutation_or_exception(
        InitialSequenceCorruption corruption) =>
        AssertInitialSequenceCorruptionAsync(corruption);

    [Fact]
    public async Task Valid_correction_chain_preserves_report_finding_response_and_report_order()
    {
        await using var context = _fixture.CreateContext();
        var seed = SeedCorrectionChain(context);
        await context.SaveChangesAsync(CancellationToken.None);

        await using var verificationContext = _fixture.CreateContext();
        var executionReport = await verificationContext.CollaborationMessages
            .SingleAsync(message => message.Id == seed.CorrectedExecutionReportId);
        var result = await ImplementerExecutionReportEligibility.ResolveAsync(
            verificationContext, executionReport, seed.RunId, seed.WorkspaceId, seed.CorrectionCheckpointId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(seed.ProposalId, result.OriginalProposal.Id);
        Assert.Equal(seed.InitialExecutionReportId, result.PreviousExecutionReport!.Id);
    }

    [Theory]
    [InlineData(CorrectionSequenceCorruption.RevisionResponseBeforeFinding)]
    [InlineData(CorrectionSequenceCorruption.CorrectedExecutionReportBeforeRevisionResponse)]
    public Task Malformed_correction_sequence_edges_are_ineligible_without_mutation_or_exception(
        CorrectionSequenceCorruption corruption) =>
        AssertCorrectionSequenceCorruptionAsync(corruption);

    private async Task AssertInitialSequenceCorruptionAsync(InitialSequenceCorruption corruption)
    {
        await using var context = _fixture.CreateContext();
        var seed = SeedInitialChain(context);
        await context.SaveChangesAsync(CancellationToken.None);
        var messageId = corruption == InitialSequenceCorruption.ExecutionReportBeforeProposal
            ? seed.ExecutionReportId
            : seed.AcceptanceId;
        await SetSequenceAsync(context, messageId, -1L);

        await using var verificationContext = _fixture.CreateContext();
        var executionReport = await verificationContext.CollaborationMessages
            .SingleAsync(message => message.Id == seed.ExecutionReportId);
        var result = await ImplementerExecutionReportEligibility.ResolveAsync(
            verificationContext, executionReport, seed.RunId, seed.WorkspaceId, seed.ResultCheckpointId, CancellationToken.None);

        Assert.Null(result);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.All(verificationContext.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    private async Task AssertCorrectionSequenceCorruptionAsync(CorrectionSequenceCorruption corruption)
    {
        await using var context = _fixture.CreateContext();
        var seed = SeedCorrectionChain(context);
        await context.SaveChangesAsync(CancellationToken.None);
        var messageId = corruption == CorrectionSequenceCorruption.RevisionResponseBeforeFinding
            ? seed.RevisionResponseId
            : seed.CorrectedExecutionReportId;
        await SetSequenceAsync(context, messageId, -1L);

        await using var verificationContext = _fixture.CreateContext();
        var executionReport = await verificationContext.CollaborationMessages
            .SingleAsync(message => message.Id == seed.CorrectedExecutionReportId);
        var result = await ImplementerExecutionReportEligibility.ResolveAsync(
            verificationContext, executionReport, seed.RunId, seed.WorkspaceId, seed.CorrectionCheckpointId, CancellationToken.None);

        Assert.Null(result);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.All(verificationContext.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    private static async Task SetSequenceAsync(DevalCopilotDbContext context, Guid messageId, long sequence)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE collaboration_messages SET Sequence = {sequence} WHERE Id = {messageId}", CancellationToken.None);
    }

    private static InitialSeed SeedInitialChain(DevalCopilotDbContext context)
    {
        var project = Project.Register(Guid.NewGuid(), "Initial chronology project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Implement the accepted plan", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "main", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var startingCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), StartingFingerprint, []);
        var resultCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), ResultFingerprint, []);

        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, StartingFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        planner.MarkAgentDispatched(Now);
        planner.CompleteAgent(AgentOutcome.Proposed, StartingFingerprint, Now);
        var proposal = CollaborationMessage.RecordAgent(
            planner, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "Implement the accepted plan.", ProposalJson, Now);

        var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, startingCheckpoint.Id, StartingFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        acceptanceAttempt.MarkAgentDispatched(Now);
        acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, StartingFingerprint, Now);
        var acceptance = CollaborationMessage.RecordAgent(
            acceptanceAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Acceptance, proposal.Id, "Accepted the plan.", AcceptanceJson, Now);

        var implementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 3, workspace.Id, startingCheckpoint.Id, StartingFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        implementation.MarkAgentDispatched(Now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now);
        var executionReport = CollaborationMessage.RecordAgent(
            implementation, Guid.NewGuid(), proposal.Actor, CollaborationMessageType.ExecutionReport, proposal.Id,
            "Implemented the accepted plan.", ExecutionReportJson, Now);

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.AddRange(startingCheckpoint, resultCheckpoint);
        context.Attempts.AddRange(planner, acceptanceAttempt, implementation);
        context.CollaborationMessages.AddRange(proposal, acceptance, executionReport);
        context.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, acceptance.Id, 1));

        return new InitialSeed(run.Id, workspace.Id, resultCheckpoint.Id, proposal.Id, acceptance.Id, executionReport.Id);
    }

    private static CorrectionSeed SeedCorrectionChain(DevalCopilotDbContext context)
    {
        var project = Project.Register(Guid.NewGuid(), "Correction chronology project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the reviewed implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "main", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var startingCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), StartingFingerprint, []);
        var implementationCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), ResultFingerprint, []);
        var correctionCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 3, Now.AddMinutes(2), new string('c', 40), CorrectionFingerprint, []);

        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, StartingFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        planner.MarkAgentDispatched(Now);
        planner.CompleteAgent(AgentOutcome.Proposed, StartingFingerprint, Now);
        var proposal = CollaborationMessage.RecordAgent(
            planner, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "Implement the plan.", ProposalJson, Now);

        var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, startingCheckpoint.Id, StartingFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        acceptanceAttempt.MarkAgentDispatched(Now);
        acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, StartingFingerprint, Now);
        var acceptance = CollaborationMessage.RecordAgent(
            acceptanceAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Acceptance, proposal.Id, "Accepted the plan.", AcceptanceJson, Now);

        var implementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 3, workspace.Id, startingCheckpoint.Id, StartingFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        implementation.MarkAgentDispatched(Now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, implementationCheckpoint.Id, Now);
        var initialReport = CollaborationMessage.RecordAgent(
            implementation, Guid.NewGuid(), proposal.Actor, CollaborationMessageType.ExecutionReport, proposal.Id,
            "Implemented the plan.", ExecutionReportJson, Now);

        var review = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 4, workspace.Id, implementationCheckpoint.Id, ResultFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        review.MarkAgentDispatched(Now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, ResultFingerprint, Now);
        var finding = CollaborationMessage.RecordAgent(
            review, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.ReviewFinding, initialReport.Id, "Add the missing guard.", ReviewFindingJson, Now);

        var correction = Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), run.Id, 5, workspace.Id, implementationCheckpoint.Id, ResultFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now);
        correction.MarkAgentDispatched(Now);
        var revisionResponse = CollaborationMessage.RecordAgent(
            correction, Guid.NewGuid(), proposal.Actor, CollaborationMessageType.RevisionResponse, finding.Id,
            "Applied the requested correction.", RevisionResponseJson, Now);
        var correctedReport = CollaborationMessage.RecordAgent(
            correction, Guid.NewGuid(), proposal.Actor, CollaborationMessageType.ExecutionReport, proposal.Id,
            "Correction completed.", ExecutionReportJson, Now);
        correction.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, correctionCheckpoint.Id, Now);

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.AddRange(startingCheckpoint, implementationCheckpoint, correctionCheckpoint);
        context.Attempts.AddRange(planner, acceptanceAttempt, implementation, review, correction);
        context.CollaborationMessages.AddRange(proposal, acceptance, initialReport, finding, revisionResponse, correctedReport);
        context.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, acceptance.Id, 1),
            AttemptInputMessage.Record(Guid.NewGuid(), review.Id, initialReport.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), correction.Id, initialReport.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), correction.Id, finding.Id, 1));

        return new CorrectionSeed(
            run.Id, workspace.Id, correctionCheckpoint.Id, proposal.Id, initialReport.Id,
            finding.Id, revisionResponse.Id, correctedReport.Id);
    }

    private sealed record InitialSeed(
        Guid RunId, Guid WorkspaceId, Guid ResultCheckpointId, Guid ProposalId, Guid AcceptanceId, Guid ExecutionReportId);

    private sealed record CorrectionSeed(
        Guid RunId,
        Guid WorkspaceId,
        Guid CorrectionCheckpointId,
        Guid ProposalId,
        Guid InitialExecutionReportId,
        Guid FindingId,
        Guid RevisionResponseId,
        Guid CorrectedExecutionReportId);

    public enum InitialSequenceCorruption
    {
        ExecutionReportBeforeProposal,
        AcceptanceBeforeProposal,
    }

    public enum CorrectionSequenceCorruption
    {
        RevisionResponseBeforeFinding,
        CorrectedExecutionReportBeforeRevisionResponse,
    }

    private const string ProposalJson =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Apply the plan\",\"risks\":\"None\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None\"}";

    private const string AcceptanceJson = "{\"rationale\":\"The plan is complete\"}";

    private const string ReviewFindingJson =
        "{\"severity\":\"high\",\"category\":\"correctness\",\"evidence\":\"The guard is missing\",\"requiredChange\":\"Add the guard\"}";

    private const string RevisionResponseJson =
        "{\"disposition\":\"Fixed\",\"evidence\":\"The guard was added\",\"resultingSourceChanges\":\"Updated the implementation\"}";

    private const string ExecutionReportJson =
        "{\"completedWork\":\"Applied the plan\",\"verification\":\"Tests passed\"}";
}
