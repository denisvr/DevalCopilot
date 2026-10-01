using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.PlanningAuthorizationTestSupport;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The explicit human authorization of one implementation claim for an escalated final plan (ADR-0016):
/// what it records, what it refuses and why, its idempotency, and its fresh-authority behavior at the short serialized
/// commit. Real file-backed SQLite with production migrations; no provider is ever involved.</summary>
public sealed class AuthorizePlanningImplementationCommandHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task It_records_one_canonical_human_instruction_and_one_grant_and_claims_nothing()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var attemptsBefore = await AgentAttemptCountAsync(dbContext, lineage.RunId);
        var evidence = new CountingEvidenceReader();

        var result = await AuthorizeHandler(dbContext, evidence).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Authorized", result.Value.Status);
        Assert.Equal(lineage.FinalProposal.Id, result.Value.FinalProposalMessageId);
        Assert.Equal(lineage.Escalation.Id, result.Value.EscalationMessageId);
        Assert.Equal(1, evidence.Captures);

        await using var verify = _fixture.CreateContext();
        var grant = await GrantAsync(verify, lineage.RunId);
        Assert.Equal(result.Value.AuthorizationId, grant.Id);
        Assert.Equal(lineage.Scene.Workspace.Id, grant.WorkspaceId);
        Assert.Equal(lineage.Scene.Checkpoint.Id, grant.CheckpointId);
        Assert.Equal(Fingerprint, grant.FingerprintSha256);
        Assert.Null(grant.ConsumedByAttemptId);
        Assert.Null(grant.ConsumedAtUtc);

        var message = await verify.CollaborationMessages.AsNoTracking().SingleAsync(candidate => candidate.Id == grant.HumanInstructionMessageId);
        Assert.Equal(CollaborationMessageType.HumanInstruction, message.Type);
        Assert.Equal(CollaborationMessageProvenance.HumanSubmitted, message.Provenance);
        Assert.Null(message.AttemptId);
        Assert.Equal(ParticipantIdentity.ForHuman(), message.Actor);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), message.Recipient);
        Assert.Equal(lineage.Escalation.Id, message.InReplyToMessageId);
        Assert.Equal(PlanningImplementationInstruction.Summary, message.Summary);
        Assert.Equal(PlanningImplementationInstruction.BuildStructuredContentJson(Rationale), message.StructuredContentJson);

        // Nothing is claimed, reserved, sealed, or invoked.
        Assert.Equal(attemptsBefore, await AgentAttemptCountAsync(verify, lineage.RunId));
        Assert.Empty(verify.Artifacts.Where(artifact => artifact.RunId == lineage.RunId));
        Assert.Empty(verify.ReviewCorrectionAuthorizations);
        var recorded = Assert.Single(verify.Events.AsNoTracking().Where(
            candidate => candidate.EventType == RunEventType.CollaborationMessageRecorded
                && candidate.PayloadJson.Contains(message.Id.ToString())));
        Assert.Null(recorded.AttemptId);
        Assert.Equal(recorded.Sequence, result.Value.LatestEventSequence);
    }

    [Fact]
    public async Task The_rationale_is_normalized_before_it_is_stored()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);

        var result = await AuthorizeAsync(dbContext, lineage, "  Café accepted.\r\nSecond line.  ");

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var message = await verify.CollaborationMessages.AsNoTracking().SingleAsync(
            candidate => candidate.Id == result.Value.HumanInstructionMessageId);
        Assert.Equal("Café accepted.\nSecond line.", PlanningImplementationInstruction.TryReadRationale(message.StructuredContentJson));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("bad\u0007control")]
    [InlineData("The password is hunter2")]
    public async Task An_invalid_rationale_is_refused_before_any_read_or_external_work_without_echo(string rationale)
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var evidence = new CountingEvidenceReader();

        var result = await AuthorizeHandler(dbContext, evidence).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, rationale), CancellationToken.None);

        var error = Assert.Single(result.Errors);
        Assert.Equal(PlanningImplementationAuthorizationErrors.RationaleInvalidCode, error.Code);
        Assert.DoesNotContain("hunter2", error.Description, StringComparison.Ordinal);
        Assert.Equal(0, evidence.Captures);
        Assert.Empty(dbContext.PlanningImplementationAuthorizations);
    }

    [Fact]
    public async Task A_rationale_longer_than_six_hundred_code_units_is_refused_and_exactly_six_hundred_is_accepted()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);

        var tooLong = await AuthorizeAsync(dbContext, lineage, new string('x', 601));
        var exact = await AuthorizeAsync(dbContext, lineage, new string('x', 600));

        Assert.Equal(PlanningImplementationAuthorizationErrors.RationaleInvalidCode, Assert.Single(tooLong.Errors).Code);
        Assert.True(exact.IsSuccess);
    }

    [Fact]
    public async Task The_command_validator_rejects_missing_and_overlong_rationales_with_the_fixed_code()
    {
        var validator = new AuthorizePlanningImplementationCommandValidator();

        foreach (var rationale in new[] { string.Empty, " ", new string('y', 601) })
        {
            var validation = await validator.ValidateAsync(new AuthorizePlanningImplementationCommand(Guid.NewGuid(), Guid.NewGuid(), rationale));
            var failure = Assert.Single(validation.Errors);
            Assert.Equal(PlanningImplementationAuthorizationErrors.RationaleInvalidCode, failure.ErrorCode);
            Assert.True(rationale.Trim().Length == 0 || !failure.ErrorMessage.Contains(rationale.Trim(), StringComparison.Ordinal));
        }

        Assert.True((await validator.ValidateAsync(new AuthorizePlanningImplementationCommand(Guid.NewGuid(), Guid.NewGuid(), Rationale))).IsValid);
    }

    [Fact]
    public async Task An_unknown_escalation_a_foreign_run_escalation_and_a_non_escalation_message_are_not_a_source()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        await using var otherContext = _fixture.CreateContext();
        var foreign = await SeedEscalatedLineageAsync(otherContext, seedCapabilities: false);
        var evidence = new CountingEvidenceReader();

        foreach (var messageId in new[] { Guid.NewGuid(), foreign.Escalation.Id, lineage.FinalProposal.Id })
        {
            var result = await AuthorizeHandler(dbContext, evidence).HandleAsync(
                new AuthorizePlanningImplementationCommand(lineage.RunId, messageId, Rationale), CancellationToken.None);

            Assert.Equal(PlanningImplementationAuthorizationErrors.SourceNotFoundCode, Assert.Single(result.Errors).Code);
        }

        Assert.Equal(0, evidence.Captures);
        Assert.Empty(dbContext.PlanningImplementationAuthorizations);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        await using var dbContext = _fixture.CreateContext();

        var result = await AuthorizeHandler(dbContext).HandleAsync(
            new AuthorizePlanningImplementationCommand(Guid.NewGuid(), Guid.NewGuid(), Rationale), CancellationToken.None);

        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    public static TheoryData<string> ForgedEscalations => new()
    {
        "summary",
        "content",
        "recipient",
        "actor",
        "reply-to-first-revision",
        "reply-to-missing",
        "attempt-owned",
        "protocol",
    };

    [Theory]
    [MemberData(nameof(ForgedEscalations))]
    public async Task A_forged_or_non_canonical_escalation_fails_closed_before_any_external_work(string forgery)
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var id = lineage.Escalation.Id;
        var messages = dbContext.CollaborationMessages.Where(message => message.Id == id);
        switch (forgery)
        {
            case "summary":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.Summary, "Some other summary."));
                break;
            case "content":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(
                    message => message.StructuredContentJson,
                    message => message.StructuredContentJson.Replace("Neither is chosen", "Both are chosen")));
                break;
            case "recipient":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.RecipientKind, ParticipantKind.Orchestrator));
                break;
            case "actor":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.ActorKind, ParticipantKind.Human));
                break;
            case "reply-to-first-revision":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.InReplyToMessageId, lineage.First.RevisedProposal.Id));
                break;
            case "reply-to-missing":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.InReplyToMessageId, Guid.NewGuid()));
                break;
            case "attempt-owned":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.AttemptId, lineage.Second.Attempt.Id));
                break;
            default:
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.ProtocolVersion, "9.9"));
                break;
        }

        var evidence = new CountingEvidenceReader();
        await using var handlerContext = _fixture.CreateContext();
        var result = await AuthorizeHandler(handlerContext, evidence).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, id, Rationale), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceInvalidCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Empty(handlerContext.PlanningImplementationAuthorizations);
        Assert.Empty(handlerContext.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.HumanInstruction));
    }

    [Fact]
    public async Task A_second_escalation_answering_the_same_final_proposal_makes_the_source_ambiguous()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        dbContext.CollaborationMessages.Add(PlanningEscalation.Record(
            lineage.RunId, lineage.Root.Id, lineage.First.RevisedProposal.Id, lineage.FinalProposal.Id,
            lineage.SecondReview.Outputs.Select(challenge => challenge.Id).ToArray(), Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await AuthorizeAsync(dbContext, lineage);

        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceInvalidCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task An_escalation_replying_to_a_first_revision_lineage_is_not_a_depth_two_source()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        var escalation = PlanningEscalation.Record(
            scene.Run.Id, root.Id, first.RevisedProposal.Id, first.RevisedProposal.Id, [Guid.NewGuid()], Now);
        dbContext.CollaborationMessages.Add(escalation);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await AuthorizeHandler(dbContext).HandleAsync(
            new AuthorizePlanningImplementationCommand(scene.Run.Id, escalation.Id, Rationale), CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceInvalidCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_lineage_with_a_missing_second_round_decision_is_not_a_source()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext, secondChallengeCount: 2);
        await dbContext.CollaborationMessages.Where(message => message.Id == lineage.Second.Decisions[1].Id).ExecuteDeleteAsync();

        var result = await AuthorizeAsync(dbContext, lineage);

        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceInvalidCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_duplicated_root_proposal_makes_the_source_invalid()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var rootOwner = lineage.Seeder; // keep the seeder alive for the duplicate below
        dbContext.CollaborationMessages.Add(CollaborationMessage.Record(
            Guid.NewGuid(), lineage.RunId, await dbContext.CollaborationMessages
                .Where(message => message.Id == lineage.Root.Id).Select(message => message.AttemptId!.Value).SingleAsync(),
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "A second proposal by the same attempt.",
            PlanningLineageSeeder.ProposalJson("Duplicate"), CollaborationMessageProvenance.ProviderObserved, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await AuthorizeAsync(dbContext, lineage);

        Assert.NotNull(rootOwner);
        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceInvalidCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_newer_independent_planner_proposal_supersedes_the_source()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        lineage.Seeder.AddRoot();
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var evidence = new CountingEvidenceReader();

        var result = await AuthorizeHandler(dbContext, evidence).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceStaleCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Empty(dbContext.PlanningImplementationAuthorizations);
    }

    [Fact]
    public async Task A_newer_checkpoint_makes_the_source_stale()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), lineage.Scene.Workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await AuthorizeAsync(dbContext, lineage);

        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceStaleCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_changed_live_fingerprint_is_refused_without_a_grant()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var handler = new AuthorizePlanningImplementationCommandHandler(
            dbContext, new FingerprintEvidenceReader(new string('c', 64)), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.ContextNotCurrentCode, Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.PlanningImplementationAuthorizations);
    }

    [Theory]
    [InlineData("lifecycle")]
    [InlineData("workspace")]
    [InlineData("lease")]
    [InlineData("mode")]
    public async Task Run_state_workspace_lease_and_mode_gates_refuse_the_authorization(string gate)
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        switch (gate)
        {
            case "lifecycle":
                await dbContext.Runs.Where(run => run.Id == lineage.RunId)
                    .ExecuteUpdateAsync(set => set.SetProperty(run => run.Lifecycle, RunLifecycle.Failed));
                break;
            case "workspace":
                await dbContext.GitWorkspaces.Where(workspace => workspace.Id == lineage.Scene.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.Status, WorkspaceStatus.NeedsAttention));
                break;
            case "lease":
                await dbContext.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == lineage.Scene.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));
                break;
            default:
                await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, lineage.RunId, (int)RunExecutionMode.Simulated);
                break;
        }

        await using var handlerContext = _fixture.CreateContext();
        var result = await AuthorizeAsync(handlerContext, lineage);

        Assert.True(result.IsFailure, gate);
        Assert.Empty(handlerContext.PlanningImplementationAuthorizations);
    }

    [Fact]
    public async Task An_identical_retry_returns_the_recorded_authorization_without_another_message_or_event()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var first = await AuthorizeAsync(dbContext, lineage);

        await using var retryContext = _fixture.CreateContext();
        var retry = await AuthorizeAsync(retryContext, lineage);

        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value.AuthorizationId, retry.Value.AuthorizationId);
        Assert.Equal(first.Value.HumanInstructionMessageId, retry.Value.HumanInstructionMessageId);
        Assert.Equal(first.Value.LatestEventSequence, retry.Value.LatestEventSequence);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.PlanningImplementationAuthorizations);
        Assert.Single(verify.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.HumanInstruction));
        Assert.Single(verify.Events.Where(candidate => candidate.PayloadJson.Contains(first.Value.HumanInstructionMessageId.ToString())));
    }

    [Fact]
    public async Task A_different_rationale_for_the_same_escalation_conflicts_and_changes_nothing()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        await AuthorizeAsync(dbContext, lineage);

        await using var retryContext = _fixture.CreateContext();
        var conflict = await AuthorizeAsync(retryContext, lineage, "A different reason entirely.");

        var error = Assert.Single(conflict.Errors);
        Assert.Equal(PlanningImplementationAuthorizationErrors.RationaleConflictCode, error.Code);
        Assert.DoesNotContain("different reason", error.Description, StringComparison.Ordinal);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.PlanningImplementationAuthorizations);
        Assert.Single(verify.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.HumanInstruction));
    }

    [Fact]
    public async Task A_stale_or_consumed_grant_is_never_revived_by_a_retry()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        await AuthorizeAsync(dbContext, lineage);
        lineage.Seeder.AddRoot();
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await using var staleContext = _fixture.CreateContext();
        var stale = await AuthorizeAsync(staleContext, lineage);
        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceStaleCode, Assert.Single(stale.Errors).Code);

        await using var other = _fixture.CreateContext();
        var consumedLineage = await SeedEscalatedLineageAsync(other, seedCapabilities: false);
        await AuthorizeAsync(other, consumedLineage);
        var claim = await ClaimAsync(other, consumedLineage);
        Assert.True(claim.IsSuccess);

        await using var retryContext = _fixture.CreateContext();
        var consumed = await AuthorizeAsync(retryContext, consumedLineage);
        Assert.Equal(PlanningImplementationAuthorizationErrors.AlreadyConsumedCode, Assert.Single(consumed.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(2, await verify.PlanningImplementationAuthorizations.CountAsync());
    }

    [Fact]
    public async Task A_corrupted_recorded_instruction_fails_closed_for_a_retry()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var first = await AuthorizeAsync(dbContext, lineage);
        await dbContext.CollaborationMessages.Where(message => message.Id == first.Value.HumanInstructionMessageId)
            .ExecuteUpdateAsync(set => set.SetProperty(message => message.StructuredContentJson, "{\"instruction\":\"x\",\"rationale\":\"y\"}"));

        await using var retryContext = _fixture.CreateContext();
        var retry = await AuthorizeAsync(retryContext, lineage);

        Assert.Equal(PlanningImplementationAuthorizationErrors.RecordedInvalidCode, Assert.Single(retry.Errors).Code);
    }

    [Fact]
    public async Task Concurrent_identical_requests_record_exactly_one_authorization()
    {
        await using var seedContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seedContext);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await AuthorizeAsync(context, lineage);
        }));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Single(results.Select(result => result.Value.AuthorizationId).Distinct());
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.PlanningImplementationAuthorizations);
        Assert.Single(verify.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.HumanInstruction));
    }

    // ---- The short serialized commit reads authority afresh -----------------------------------------------------

    [Fact]
    public async Task Git_evidence_is_captured_before_the_transaction_begins()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var order = new List<string>();
        var faulting = new FaultInjectingDbContext(dbContext) { BeforeBeginTransaction = _ => { order.Add("begin"); return Task.CompletedTask; } };
        var evidence = new CountingEvidenceReader(() => { order.Add("capture"); return Task.CompletedTask; });

        var result = await AuthorizeHandler(faulting, evidence).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["capture", "begin"], order);
    }

    [Fact]
    public async Task A_newer_planner_proposal_committed_before_begin_is_seen_and_refuses_the_authorization()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var other = _fixture.CreateContext();
                SeederFor(other, lineage.Scene, lineage.Seeder.NextAttemptNumber + 10).AddRoot();
                await other.SaveChangesAsync(CancellationToken.None);
            },
        };

        var result = await AuthorizeHandler(faulting).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceStaleCode, Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.PlanningImplementationAuthorizations);
        Assert.Empty(verify.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.HumanInstruction));
    }

    [Fact]
    public async Task A_checkpoint_committed_before_begin_is_seen_and_refuses_the_authorization()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var other = _fixture.CreateContext();
                other.GitCheckpoints.Add(GitCheckpoint.Capture(
                    Guid.NewGuid(), lineage.Scene.Workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []));
                await other.SaveChangesAsync(CancellationToken.None);
            },
        };

        var result = await AuthorizeHandler(faulting).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.ContextNotCurrentCode, Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.PlanningImplementationAuthorizations);
    }

    [Fact]
    public async Task A_run_that_stopped_running_before_begin_is_seen_and_refuses_the_authorization()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var other = _fixture.CreateContext();
                await other.Runs.Where(run => run.Id == lineage.RunId)
                    .ExecuteUpdateAsync(set => set.SetProperty(run => run.Lifecycle, RunLifecycle.Failed));
            },
        };

        var result = await AuthorizeHandler(faulting).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.Equal("runs.not_running", Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.PlanningImplementationAuthorizations);
    }

    [Fact]
    public async Task An_identical_authorization_committed_before_begin_is_reused_without_a_second_message()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        Guid? competingAuthorizationId = null;
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var other = _fixture.CreateContext();
                competingAuthorizationId = (await AuthorizeAsync(other, lineage)).Value.AuthorizationId;
            },
        };

        var result = await AuthorizeHandler(faulting).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(competingAuthorizationId, result.Value.AuthorizationId);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.PlanningImplementationAuthorizations);
        Assert.Single(verify.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.HumanInstruction));
    }

    [Fact]
    public async Task A_conflicting_authorization_committed_before_begin_is_a_rationale_conflict()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var other = _fixture.CreateContext();
                await AuthorizeAsync(other, lineage, "A competing human reason.");
            },
        };

        var result = await AuthorizeHandler(faulting).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.RationaleConflictCode, Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.PlanningImplementationAuthorizations);
    }

    [Fact]
    public async Task A_failed_save_leaves_no_message_grant_or_event()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var eventsBefore = await dbContext.Events.CountAsync();
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave,
        };

        var result = await AuthorizeHandler(faulting).HandleAsync(
            new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.Escalation.Id, Rationale), CancellationToken.None);

        Assert.Equal("planning_authorizations.persistence_failed", Assert.Single(result.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.PlanningImplementationAuthorizations);
        Assert.Empty(verify.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.HumanInstruction));
        Assert.Equal(eventsBefore, await verify.Events.CountAsync());
    }

    private sealed class FingerprintEvidenceReader(string fingerprint) : Application.Features.Projects.Ports.IGitWorkspaceEvidenceReader
    {
        public Task<Application.Features.Projects.Ports.GitWorkspaceEvidenceResult> CaptureAsync(
            string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new Application.Features.Projects.Ports.GitWorkspaceEvidenceResult(
                Application.Features.Projects.Ports.GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprint, [], null));
    }
}
