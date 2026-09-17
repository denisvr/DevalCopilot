using DevalCopilot.Application.Features.Runs.Commands.RecordCollaborationMessage;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RecordCollaborationMessageCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_records_the_message_and_journal_event_atomically()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateRunningAttempt();
        dbContext.AddRange(project, run, attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(CreateProposalCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(result.Value.MessageId, Assert.Single(dbContext.CollaborationMessages.Where(message => message.RunId == run.Id)).Id);
        var eventRecord = Assert.Single(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
        Assert.Equal(RunEventType.CollaborationMessageRecorded, eventRecord.EventType);
        Assert.Equal(result.Value.EventSequence, eventRecord.Sequence);
    }

    [Fact]
    public async Task HandleAsync_rejects_cross_run_replies_without_writing_a_message_or_event()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateRunningAttempt();
        var (otherProject, otherRun, otherAttempt) = CreateRunningAttempt();
        dbContext.AddRange(project, run, attempt, otherProject, otherRun, otherAttempt);
        var otherMessage = CollaborationMessage.Record(
            Guid.NewGuid(), otherRun.Id, otherAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
            "Other proposal", ProposalContent, CollaborationMessageProvenance.Simulated, Now);
        dbContext.CollaborationMessages.Add(otherMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            CreateChallengeCommand(run.Id, attempt.Id, otherMessage.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("collaboration_messages.reply_not_found", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_requires_a_decision_to_resolve_an_existing_proposal_or_challenge()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateRunningAttempt();
        dbContext.AddRange(project, run, attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            CreateProposalCommand(run.Id, attempt.Id) with
            {
                Type = CollaborationMessageType.Decision,
                StructuredContentJson = DecisionContent,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("collaboration_messages.reply_required", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_rejects_an_unknown_protocol_version_without_persisting_a_fact()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateRunningAttempt();
        dbContext.AddRange(project, run, attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            CreateProposalCommand(run.Id, attempt.Id) with { ProtocolVersion = "9.9" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("collaboration_messages.invalid", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_returns_a_safe_result_for_an_undefined_message_type_without_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateRunningAttempt();
        dbContext.AddRange(project, run, attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            CreateProposalCommand(run.Id, attempt.Id) with { Type = (CollaborationMessageType)999 },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("collaboration_messages.invalid", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_returns_a_safe_failure_for_malformed_structured_content()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateRunningAttempt();
        dbContext.AddRange(project, run, attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            CreateProposalCommand(run.Id, attempt.Id) with { StructuredContentJson = "{" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("collaboration_messages.invalid", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_rejects_invalid_parent_and_self_reply_without_new_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateRunningAttempt();
        dbContext.AddRange(project, run, attempt);
        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, attempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
            "Existing proposal", ProposalContent, CollaborationMessageProvenance.Simulated, Now);
        dbContext.CollaborationMessages.Add(proposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now));
        var invalidParent = await handler.HandleAsync(
            CreateMessageCommand(run.Id, attempt.Id, CollaborationMessageType.ExecutionReport, proposal.Id), CancellationToken.None);
        var selfReply = await handler.HandleAsync(
            CreateMessageCommand(run.Id, attempt.Id, CollaborationMessageType.Challenge, proposal.Id) with
            {
                MessageId = proposal.Id,
            },
            CancellationToken.None);

        Assert.Equal("collaboration_messages.invalid_reply_parent", Assert.Single(invalidParent.Errors).Code);
        Assert.Equal("collaboration_messages.self_reply", Assert.Single(selfReply.Errors).Code);
        Assert.Single(dbContext.CollaborationMessages.Where(message => message.RunId == run.Id));
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_records_a_valid_proposal_to_revision_response_flow()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateRunningAttempt();
        dbContext.AddRange(project, run, attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordCollaborationMessageCommandHandler(dbContext, new FixedTimeProvider(Now));
        var proposal = await RecordAsync(CollaborationMessageType.Proposal, null);
        var challenge = await RecordAsync(CollaborationMessageType.Challenge, proposal.MessageId);
        var decision = await RecordAsync(CollaborationMessageType.Decision, challenge.MessageId);
        var execution = await RecordAsync(CollaborationMessageType.ExecutionReport, decision.MessageId);
        var finding = await RecordAsync(CollaborationMessageType.ReviewFinding, execution.MessageId);
        var revision = await RecordAsync(CollaborationMessageType.RevisionResponse, finding.MessageId);

        Assert.Equal(
            revision.MessageId,
            dbContext.CollaborationMessages.Where(message => message.RunId == run.Id).OrderBy(message => message.Sequence).Last().Id);
        Assert.Equal(6, dbContext.CollaborationMessages.Count(message => message.RunId == run.Id));
        Assert.Equal(6, dbContext.Events.Count(runEvent => runEvent.RunId == run.Id));

        async Task<RecordCollaborationMessageCommandResult> RecordAsync(CollaborationMessageType type, Guid? replyToMessageId)
        {
            var result = await handler.HandleAsync(CreateMessageCommand(run.Id, attempt.Id, type, replyToMessageId), CancellationToken.None);
            Assert.True(result.IsSuccess);
            return result.Value;
        }
    }

    private static (Project Project, Run Run, Attempt Attempt) CreateRunningAttempt()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Record ledger", Now);
        run.Claim(Now);
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        return (project, run, attempt);
    }

    private static RecordCollaborationMessageCommand CreateProposalCommand(Guid runId, Guid attemptId)
    {
        return new RecordCollaborationMessageCommand(
            Guid.NewGuid(),
            runId,
            attemptId,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex,
            ParticipantKind.Claude,
            CollaborationMessageType.Proposal,
            null,
            "Propose the ledger.",
            ProposalContent,
            CollaborationMessageProvenance.Simulated);
    }

    private static RecordCollaborationMessageCommand CreateChallengeCommand(Guid runId, Guid attemptId, Guid replyToMessageId)
    {
        return new RecordCollaborationMessageCommand(
            Guid.NewGuid(),
            runId,
            attemptId,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Claude,
            ParticipantKind.Codex,
            CollaborationMessageType.Challenge,
            replyToMessageId,
            "Challenge the proposal.",
            ChallengeContent,
            CollaborationMessageProvenance.Simulated);
    }

    private static RecordCollaborationMessageCommand CreateMessageCommand(
        Guid runId,
        Guid attemptId,
        CollaborationMessageType type,
        Guid? replyToMessageId)
    {
        var (actor, recipient) = type switch
        {
            CollaborationMessageType.Proposal or CollaborationMessageType.Decision or CollaborationMessageType.ReviewFinding =>
                (ParticipantKind.Codex, ParticipantKind.Claude),
            _ => (ParticipantKind.Claude, ParticipantKind.Codex),
        };

        return new RecordCollaborationMessageCommand(
            Guid.NewGuid(),
            runId,
            attemptId,
            CollaborationMessage.ProtocolVersionOne,
            actor,
            recipient,
            type,
            replyToMessageId,
            $"{type} summary.",
            ContentFor(type),
            CollaborationMessageProvenance.Simulated);
    }

    private static string ContentFor(CollaborationMessageType type)
    {
        return type switch
        {
            CollaborationMessageType.Proposal => ProposalContent,
            CollaborationMessageType.Challenge => ChallengeContent,
            CollaborationMessageType.Decision => DecisionContent,
            CollaborationMessageType.ExecutionReport => ExecutionReportContent,
            CollaborationMessageType.ReviewFinding => ReviewFindingContent,
            CollaborationMessageType.RevisionResponse => RevisionResponseContent,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"assumptions\":\"Host is authoritative\",\"verification\":\"Tests\",\"risks\":\"Unbounded content\"}";
    private const string DecisionContent =
        "{\"resolution\":\"Accepted\",\"rationale\":\"Evidence\",\"resultingPlanChanges\":\"Use ledger\",\"nextAction\":\"Continue\"}";
    private const string ChallengeContent =
        "{\"disputedItem\":\"Claim\",\"materialImpact\":\"Impact\",\"reasoning\":\"Reasoning\",\"alternativeOrQuestion\":\"Alternative\"}";
    private const string ExecutionReportContent =
        "{\"completedWork\":\"Completed\",\"verification\":\"Verified\"}";
    private const string ReviewFindingContent =
        "{\"severity\":\"Minor\",\"affectedArea\":\"Ledger\",\"expectedBehavior\":\"Bounded\",\"evidence\":\"Review\",\"requiredDisposition\":\"Fix\"}";
    private const string RevisionResponseContent =
        "{\"disposition\":\"Fixed\",\"evidence\":\"Tests\",\"resultingSourceChanges\":\"Updated policy\"}";
}
