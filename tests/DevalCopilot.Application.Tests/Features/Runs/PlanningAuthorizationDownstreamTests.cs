using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.PlanningAuthorizationTestSupport;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>What happens after the authorized claim (ADR-0016): the eligibility feed, the dispatch gate, result recording,
/// and the downstream ExecutionReport chain all prove the same consumed grant, its exact ordered inputs, and the actual
/// Planner root — and fail closed, with no provider reachable, for any tampered, mismatched, or wrongly owned fact.</summary>
public sealed class PlanningAuthorizationDownstreamTests : IAsyncLifetime
{
    private static readonly string StartingHead = new('a', 40);
    private static readonly string ChangedFingerprint = new('b', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(EscalatedLineage Lineage, Guid AttemptId, Guid AuthorizationId, Guid InstructionId)> ClaimedAsync(
        EscalationForm form = EscalationForm.Writer)
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext, secondChallengeCount: 2, form: form);
        var authorization = await AuthorizeAsync(dbContext, lineage);
        var claim = await ClaimAsync(dbContext, lineage);
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        return (lineage, claim.Value.AttemptId, authorization.Value.AuthorizationId, authorization.Value.HumanInstructionMessageId);
    }

    private static PlanningImplementationAuthorizationFact Fact(
        EscalatedLineage lineage, Guid authorizationId, Guid instructionId, string rationale = Rationale) =>
        new(authorizationId, lineage.Escalation.Id, lineage.FinalProposal.Id, instructionId, rationale);

    private async Task<Guid> RecordSuccessAsync(EscalatedLineage lineage, Guid attemptId, Guid authorizationId, Guid instructionId)
    {
        await using var dbContext = _fixture.CreateContext();
        var dispatch = await new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(
                lineage.RunId, attemptId, new ExpectedDirectHumanGuidance(null), Fact(lineage, authorizationId, instructionId)),
            CancellationToken.None);
        Assert.True(dispatch.IsSuccess, dispatch.IsFailure ? dispatch.Errors[0].Code : null);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var recorded = await new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordImplementationResultCommand(
                lineage.RunId, attemptId, true, StartingHead, ChangedFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [],
                ValidatedImplementationReport.Create(
                    "Implemented the final plan.", ["src/Foo.cs"], "Added the type.", string.Empty, string.Empty, "Run the tests."),
                null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);
        Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Errors[0].Code : null);
        Assert.Equal(AgentOutcome.Implemented, recorded.Value.Outcome);
        return await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.AttemptId == attemptId && message.Type == CollaborationMessageType.ExecutionReport)
            .Select(message => message.Id)
            .SingleAsync();
    }

    // ---- Eligibility feed ---------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(HistoricalAndCurrentForms))]
    public async Task The_feed_projects_the_consumed_authorization_for_a_coherent_attempt(EscalationForm form)
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync(form);
        await using var dbContext = _fixture.CreateContext();

        var eligible = await new GetEligibleImplementationAttemptsQueryHandler(dbContext).HandleAsync(
            new GetEligibleImplementationAttemptsQuery(), CancellationToken.None);

        var attempt = Assert.Single(eligible, candidate => candidate.AttemptId == attemptId);
        Assert.Equal(Fact(lineage, authorizationId, instructionId), attempt.PlanningAuthorization);
    }

    public static TheoryData<string> FeedTampers => new()
    {
        "wrong-consumption-owner",
        "grant-unconsumed",
        "missing-instruction-input",
        "extra-input",
        "reordered-inputs",
        "instruction-content",
        "grant-checkpoint",
    };

    private static async Task TamperAsync(DevalCopilotDbContext seed, EscalatedLineage lineage, Guid attemptId, Guid instructionId, string tamper)
    {
        var inputs = seed.AttemptInputMessages.Where(input => input.AttemptId == attemptId);
        switch (tamper)
        {
            case "wrong-consumption-owner":
                await seed.PlanningImplementationAuthorizations.Where(grant => grant.RunId == lineage.RunId)
                    .ExecuteUpdateAsync(set => set.SetProperty(grant => grant.ConsumedByAttemptId, lineage.Second.Attempt.Id));
                break;
            case "grant-unconsumed":
                await seed.PlanningImplementationAuthorizations.Where(grant => grant.RunId == lineage.RunId)
                    .ExecuteUpdateAsync(set => set.SetProperty(grant => grant.ConsumedByAttemptId, (Guid?)null)
                        .SetProperty(grant => grant.ConsumedAtUtc, (DateTimeOffset?)null));
                break;
            case "missing-instruction-input":
                await inputs.Where(input => input.CollaborationMessageId == instructionId).ExecuteDeleteAsync();
                break;
            case "extra-input":
                seed.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, lineage.Root.Id, sequence: 4));
                await seed.SaveChangesAsync(CancellationToken.None);
                break;
            case "reordered-inputs":
                await inputs.Where(input => input.Sequence == 1).ExecuteUpdateAsync(set => set.SetProperty(input => input.Sequence, 9));
                await inputs.Where(input => input.Sequence == 2).ExecuteUpdateAsync(set => set.SetProperty(input => input.Sequence, 1));
                await inputs.Where(input => input.Sequence == 9).ExecuteUpdateAsync(set => set.SetProperty(input => input.Sequence, 2));
                break;
            case "instruction-content":
                await seed.CollaborationMessages.Where(message => message.Id == instructionId)
                    .ExecuteUpdateAsync(set => set.SetProperty(message => message.StructuredContentJson, "{\"instruction\":\"x\",\"rationale\":\"y\"}"));
                break;
            default:
                await seed.PlanningImplementationAuthorizations.Where(grant => grant.RunId == lineage.RunId)
                    .ExecuteUpdateAsync(set => set.SetProperty(grant => grant.CheckpointId, Guid.NewGuid()));
                break;
        }
    }

    [Theory]
    [MemberData(nameof(FeedTampers))]
    public async Task The_feed_excludes_an_attempt_with_a_tampered_grant_or_inputs_so_no_provider_can_start(string tamper)
    {
        var (lineage, attemptId, _, instructionId) = await ClaimedAsync();
        await using (var seed = _fixture.CreateContext())
        {
            await TamperAsync(seed, lineage, attemptId, instructionId, tamper);
        }

        await using var dbContext = _fixture.CreateContext();
        var eligible = await new GetEligibleImplementationAttemptsQueryHandler(dbContext).HandleAsync(
            new GetEligibleImplementationAttemptsQuery(), CancellationToken.None);

        Assert.DoesNotContain(eligible, candidate => candidate.AttemptId == attemptId);
    }

    // ---- Fresh dispatch gate ------------------------------------------------------------------------------------

    private async Task<Result> DispatchAsync(
        DevalCopilotDbContext dbContext, EscalatedLineage lineage, Guid attemptId, PlanningImplementationAuthorizationFact? expected)
    {
        var dispatch = await new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(lineage.RunId, attemptId, new ExpectedDirectHumanGuidance(null), expected),
            CancellationToken.None);
        return new Result(dispatch.IsSuccess, dispatch.IsFailure ? dispatch.Errors[0].Code : null);
    }

    private sealed record Result(bool Succeeded, string? Code);

    [Theory]
    [MemberData(nameof(HistoricalAndCurrentForms))]
    public async Task The_dispatch_gate_accepts_exactly_the_durable_authorization(EscalationForm form)
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync(form);
        await using var dbContext = _fixture.CreateContext();

        var result = await DispatchAsync(dbContext, lineage, attemptId, Fact(lineage, authorizationId, instructionId));

        Assert.True(result.Succeeded, result.Code);
    }

    [Fact]
    public async Task The_dispatch_gate_refuses_a_missing_or_different_expectation()
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync();
        var good = Fact(lineage, authorizationId, instructionId);

        foreach (var wrong in new PlanningImplementationAuthorizationFact?[]
                 {
                     null,
                     good with { Rationale = "Another reason." },
                     good with { AuthorizationId = Guid.NewGuid() },
                     good with { HumanInstructionMessageId = Guid.NewGuid() },
                     good with { FinalProposalMessageId = lineage.First.RevisedProposal.Id },
                 })
        {
            await using var dbContext = _fixture.CreateContext();
            var result = await DispatchAsync(dbContext, lineage, attemptId, wrong);

            Assert.False(result.Succeeded);
            Assert.Equal(PlanningImplementationAuthorizationErrors.DispatchMismatchCode, result.Code);
            Assert.Null((await dbContext.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId)).AgentDispatchedAtUtc);
        }
    }

    [Theory]
    [MemberData(nameof(FeedTampers))]
    public async Task The_dispatch_gate_refuses_a_tampered_grant_or_inputs_even_with_the_projected_expectation(string tamper)
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync();
        await using (var seed = _fixture.CreateContext())
        {
            await TamperAsync(seed, lineage, attemptId, instructionId, tamper);
        }

        await using var dbContext = _fixture.CreateContext();
        var result = await DispatchAsync(dbContext, lineage, attemptId, Fact(lineage, authorizationId, instructionId));

        Assert.False(result.Succeeded, tamper);
        Assert.Equal(PlanningImplementationAuthorizationErrors.DispatchMismatchCode, result.Code);
    }

    [Fact]
    public async Task The_dispatch_gate_reads_the_grant_afresh_and_not_from_a_tracked_attempt()
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync();
        await using var dbContext = _fixture.CreateContext();
        // Populate the context first: the attempt and its messages are tracked before the competing change commits.
        await dbContext.Attempts.SingleAsync(attempt => attempt.Id == attemptId);
        await dbContext.CollaborationMessages.Where(message => message.RunId == lineage.RunId).ToListAsync();
        await using (var other = _fixture.CreateContext())
        {
            await TamperAsync(other, lineage, attemptId, instructionId, "wrong-consumption-owner");
        }

        var result = await DispatchAsync(dbContext, lineage, attemptId, Fact(lineage, authorizationId, instructionId));

        Assert.Equal(PlanningImplementationAuthorizationErrors.DispatchMismatchCode, result.Code);
    }

    [Fact]
    public async Task An_ordinary_implementation_attempt_is_never_dispatched_with_an_expected_authorization()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        seeder.AddReview(root, AgentOutcome.Accepted);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var claim = await new Application.Features.Runs.Commands.CreateImplementationAttempt.CreateImplementationAttemptCommandHandler(
                dbContext, new CountingEvidenceReader(), new RecordingArtifactStore(), new FixedTimeProvider(Now))
            .HandleAsync(new Application.Features.Runs.Commands.CreateImplementationAttempt.CreateImplementationAttemptCommand(scene.Run.Id, root.Id), CancellationToken.None);
        Assert.True(claim.IsSuccess);
        var bogus = new PlanningImplementationAuthorizationFact(Guid.NewGuid(), Guid.NewGuid(), root.Id, Guid.NewGuid(), Rationale);

        await using var gate = _fixture.CreateContext();
        var refused = await new MarkAgentAttemptDispatchedCommandHandler(gate, new FixedTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.Run.Id, claim.Value.AttemptId, new ExpectedDirectHumanGuidance(null), bogus),
            CancellationToken.None);
        var accepted = await new MarkAgentAttemptDispatchedCommandHandler(gate, new FixedTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.Run.Id, claim.Value.AttemptId, new ExpectedDirectHumanGuidance(null)),
            CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.DispatchMismatchCode, Assert.Single(refused.Errors).Code);
        Assert.True(accepted.IsSuccess);
    }

    // ---- Result recording ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Result_recording_refuses_an_attempt_whose_authorization_identity_no_longer_holds()
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync();
        await using (var dispatchContext = _fixture.CreateContext())
        {
            Assert.True((await DispatchAsync(dispatchContext, lineage, attemptId, Fact(lineage, authorizationId, instructionId))).Succeeded);
            await dispatchContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var seed = _fixture.CreateContext())
        {
            await TamperAsync(seed, lineage, attemptId, instructionId, "wrong-consumption-owner");
        }

        await using var dbContext = _fixture.CreateContext();
        var recorded = await new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordImplementationResultCommand(
                lineage.RunId, attemptId, true, StartingHead, ChangedFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [],
                ValidatedImplementationReport.Create("Done.", ["src/Foo.cs"], "Added.", string.Empty, string.Empty, "Run."),
                null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.ClaimInvalidCode, Assert.Single(recorded.Errors).Code);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(AttemptStatus.Running, (await verify.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId)).Status);
        Assert.Empty(verify.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.ExecutionReport));
    }

    // ---- The ExecutionReport chain ------------------------------------------------------------------------------

    public static TheoryData<EscalationForm> HistoricalAndCurrentForms => new() { EscalationForm.Legacy, EscalationForm.Current };

    [Theory]
    [MemberData(nameof(HistoricalAndCurrentForms))]
    public async Task A_successful_authorized_implementation_makes_a_valid_report_chain_rooted_at_the_actual_planner_root(
        EscalationForm form)
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync(form);
        var reportId = await RecordSuccessAsync(lineage, attemptId, authorizationId, instructionId);
        await using var dbContext = _fixture.CreateContext();
        var report = await dbContext.CollaborationMessages.AsNoTracking().SingleAsync(message => message.Id == reportId);
        var resultCheckpoint = await dbContext.Attempts.AsNoTracking()
            .Where(attempt => attempt.Id == attemptId).Select(attempt => attempt.AgentResultGitCheckpointId!.Value).SingleAsync();

        var chain = await ImplementerExecutionReportEligibility.ResolveAsync(
            dbContext, report, lineage.RunId, lineage.Scene.Workspace.Id, resultCheckpoint, CancellationToken.None);

        Assert.NotNull(chain);
        Assert.Equal(lineage.FinalProposal.Id, report.InReplyToMessageId);
        // The actual Planner root across both revisions, never the immediate depth-one parent of the final plan.
        Assert.Equal(lineage.Root.Id, chain.OriginalProposal.Id);
        Assert.NotEqual(lineage.First.RevisedProposal.Id, chain.OriginalProposal.Id);
        // The plan a review must judge is the implemented final Proposal, distinct from the root and the first revision.
        Assert.Equal(lineage.FinalProposal.Id, chain.ImplementedPlan.Id);
        Assert.NotEqual(chain.OriginalProposal.Id, chain.ImplementedPlan.Id);
        Assert.NotEqual(lineage.First.RevisedProposal.Id, chain.ImplementedPlan.Id);
        Assert.Equal(attemptId, chain.OwnerAttempt.Id);
        Assert.Null(chain.PreviousExecutionReport);
    }

    [Theory]
    [MemberData(nameof(HistoricalAndCurrentForms))]
    public async Task The_chain_validates_consent_against_the_starting_checkpoint_not_the_later_result_checkpoint(EscalationForm form)
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync(form);
        var reportId = await RecordSuccessAsync(lineage, attemptId, authorizationId, instructionId);
        await using var dbContext = _fixture.CreateContext();
        var grant = await GrantAsync(dbContext, lineage.RunId);
        var attempt = await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);

        Assert.Equal(lineage.Scene.Checkpoint.Id, grant.CheckpointId);
        Assert.Equal(grant.CheckpointId, attempt.AgentGitCheckpointId);
        Assert.NotEqual(attempt.AgentGitCheckpointId, attempt.AgentResultGitCheckpointId);
        Assert.NotNull(await ResolveAsync(dbContext, lineage, reportId, attempt));
    }

    private static async Task<ImplementerExecutionReportEligibility.Result?> ResolveAsync(
        DevalCopilotDbContext dbContext, EscalatedLineage lineage, Guid reportId, Attempt attempt)
    {
        var report = await dbContext.CollaborationMessages.AsNoTracking().SingleAsync(message => message.Id == reportId);
        return await ImplementerExecutionReportEligibility.ResolveAsync(
            dbContext, report, lineage.RunId, lineage.Scene.Workspace.Id, attempt.AgentResultGitCheckpointId!.Value, CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(HistoricalAndCurrentForms))]
    public async Task A_later_independent_planner_proposal_does_not_invalidate_the_historical_chain(EscalationForm form)
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync(form);
        var reportId = await RecordSuccessAsync(lineage, attemptId, authorizationId, instructionId);
        await using var dbContext = _fixture.CreateContext();
        SeederFor(dbContext, lineage.Scene, lineage.Seeder.NextAttemptNumber + 10).AddRoot();
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var attempt = await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);

        Assert.NotNull(await ResolveAsync(dbContext, lineage, reportId, attempt));
    }

    public static TheoryData<string> ChainTampers => new()
    {
        "wrong-consumption-owner",
        "grant-unconsumed",
        "missing-instruction-input",
        "extra-input",
        "reordered-inputs",
        "instruction-content",
        "grant-checkpoint",
        "report-replies-to-parent",
        "second-round-review-exists",
    };

    [Theory]
    [MemberData(nameof(ChainTampers))]
    public async Task A_tampered_authorization_or_input_chain_invalidates_the_report(string tamper)
    {
        var (lineage, attemptId, authorizationId, instructionId) = await ClaimedAsync();
        var reportId = await RecordSuccessAsync(lineage, attemptId, authorizationId, instructionId);
        await using (var seed = _fixture.CreateContext())
        {
            if (tamper == "report-replies-to-parent")
            {
                await seed.CollaborationMessages.Where(message => message.Id == reportId)
                    .ExecuteUpdateAsync(set => set.SetProperty(message => message.InReplyToMessageId, lineage.First.RevisedProposal.Id));
            }
            else if (tamper == "second-round-review-exists")
            {
                SeederFor(seed, lineage.Scene, lineage.Seeder.NextAttemptNumber + 10).AddReview(lineage.FinalProposal, AgentOutcome.Accepted);
                await seed.SaveChangesAsync(CancellationToken.None);
            }
            else
            {
                await TamperAsync(seed, lineage, attemptId, instructionId, tamper);
            }
        }

        await using var dbContext = _fixture.CreateContext();
        var attempt = await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);

        Assert.Null(await ResolveAsync(dbContext, lineage, reportId, attempt));
    }

    [Fact]
    public async Task A_historical_first_revision_plan_chain_keeps_its_root_and_reviews_the_revision()
    {
        await using var dbContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (_, root) = seeder.AddRoot();
        var (_, first) = seeder.AddChallengedRound(root);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var claim = await ClaimHandler(dbContext).HandleAsync(
            new Application.Features.Runs.Commands.CreateImplementationAttempt.CreateImplementationAttemptCommand(scene.Run.Id, first.RevisedProposal.Id),
            CancellationToken.None);
        Assert.True(claim.IsSuccess);
        var dispatch = await new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.Run.Id, claim.Value.AttemptId, new ExpectedDirectHumanGuidance(null)), CancellationToken.None);
        Assert.True(dispatch.IsSuccess);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var recorded = await new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordImplementationResultCommand(
                scene.Run.Id, claim.Value.AttemptId, true, StartingHead, ChangedFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [],
                ValidatedImplementationReport.Create("Done.", ["src/Foo.cs"], "Added.", string.Empty, string.Empty, "Run."),
                null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);
        Assert.True(recorded.IsSuccess);
        var report = await dbContext.CollaborationMessages.AsNoTracking()
            .SingleAsync(message => message.AttemptId == claim.Value.AttemptId && message.Type == CollaborationMessageType.ExecutionReport);
        var resultCheckpoint = await dbContext.Attempts.AsNoTracking()
            .Where(attempt => attempt.Id == claim.Value.AttemptId).Select(attempt => attempt.AgentResultGitCheckpointId!.Value).SingleAsync();

        var chain = await ImplementerExecutionReportEligibility.ResolveAsync(
            dbContext, report, scene.Run.Id, scene.Workspace.Id, resultCheckpoint, CancellationToken.None);

        Assert.NotNull(chain);
        Assert.Equal(root.Id, chain.OriginalProposal.Id);
        // ADR-0017: an ordinary first revision is reviewed as the revised Proposal it implemented; the root stays the lineage identity.
        Assert.Equal(first.RevisedProposal.Id, chain.ImplementedPlan.Id);
        Assert.NotEqual(chain.OriginalProposal.Id, chain.ImplementedPlan.Id);
        Assert.Empty(dbContext.PlanningImplementationAuthorizations);
    }
}
