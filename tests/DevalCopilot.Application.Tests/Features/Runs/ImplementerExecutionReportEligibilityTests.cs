using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class ImplementerExecutionReportEligibilityTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 11, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Resolver_lineage_requires_and_accepts_the_complete_ordered_provider_observed_chain()
    {
        await using var context = _fixture.CreateContext();
        var seed = SeedResolverLineage(context, ResolverLineageCorruption.None);
        await context.SaveChangesAsync(CancellationToken.None);

        var result = await ImplementerExecutionReportEligibility.ResolveAsync(
            context, seed.ExecutionReport, seed.RunId, seed.WorkspaceId, seed.ResultCheckpointId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(seed.OriginalProposalId, result.OriginalProposal.Id);
    }

    [Theory]
    [InlineData(ResolverLineageCorruption.SimulatedOriginalProposal)]
    [InlineData(ResolverLineageCorruption.WrongOriginalProposalActor)]
    [InlineData(ResolverLineageCorruption.ChallengeFromOtherRun)]
    [InlineData(ResolverLineageCorruption.ChallengeFromOtherReviewAttempt)]
    [InlineData(ResolverLineageCorruption.SimulatedChallenge)]
    [InlineData(ResolverLineageCorruption.WrongChallengeActor)]
    [InlineData(ResolverLineageCorruption.ReviewOfDifferentProposal)]
    [InlineData(ResolverLineageCorruption.IncompleteChallengeSet)]
    [InlineData(ResolverLineageCorruption.DuplicatedChallengeSet)]
    [InlineData(ResolverLineageCorruption.ReorderedChallengeSet)]
    [InlineData(ResolverLineageCorruption.StaleReviewCheckpoint)]
    [InlineData(ResolverLineageCorruption.ExecutionReportBeforeProposal)]
    [InlineData(ResolverLineageCorruption.ChallengeBeforeOriginalProposal)]
    [InlineData(ResolverLineageCorruption.DecisionBeforeChallenge)]
    [InlineData(ResolverLineageCorruption.RevisedProposalBeforeDecision)]
    public async Task Malformed_resolver_lineage_is_ineligible_without_mutation_or_exception(
        ResolverLineageCorruption corruption)
    {
        await using var context = _fixture.CreateContext();
        var seed = SeedResolverLineage(context, corruption);
        await context.SaveChangesAsync(CancellationToken.None);

        await MutateResolverSequenceAsync(context, seed, corruption);
        await using var verificationContext = _fixture.CreateContext();
        var executionReport = await verificationContext.CollaborationMessages
            .SingleAsync(message => message.Id == seed.ExecutionReport.Id);
        var result = await ImplementerExecutionReportEligibility.ResolveAsync(
            verificationContext, executionReport, seed.RunId, seed.WorkspaceId, seed.ResultCheckpointId, CancellationToken.None);

        Assert.Null(result);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.All(verificationContext.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    private static Seed SeedResolverLineage(DevalCopilotDbContext context, ResolverLineageCorruption corruption)
    {
        var project = Project.Register(Guid.NewGuid(), "Eligibility project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Resolve the plan", Now);
        run.Claim(Now);
        var otherRun = corruption == ResolverLineageCorruption.ChallengeFromOtherRun
            ? Run.RecordIntent(Guid.NewGuid(), project.Id, 2, "Foreign run", Now)
            : null;
        otherRun?.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "main", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var startingCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var resultCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []);

        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        planner.MarkAgentDispatched(Now);
        planner.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now);
        var originalProposal = corruption == ResolverLineageCorruption.SimulatedOriginalProposal
            ? CollaborationMessage.Record(
                Guid.NewGuid(), run.Id, planner.Id, CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
                "Original plan.", ProposalJson, CollaborationMessageProvenance.Simulated, Now)
            : CollaborationMessage.Record(
                Guid.NewGuid(), run.Id, planner.Id, CollaborationMessage.ProtocolVersionOne,
                corruption == ResolverLineageCorruption.WrongOriginalProposalActor
                    ? ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.ClaudeCode)
                    : ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
                "Original plan.", ProposalJson, CollaborationMessageProvenance.ProviderObserved, Now);

        var reviewCheckpoint = corruption == ResolverLineageCorruption.StaleReviewCheckpoint
            ? GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 3, Now.AddMinutes(2), new string('c', 40), new string('c', 64), [])
            : startingCheckpoint;
        var review = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, reviewCheckpoint.Id, reviewCheckpoint.FingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        review.MarkAgentDispatched(Now);
        review.CompleteAgent(AgentOutcome.Challenged, reviewCheckpoint.FingerprintSha256, Now);

        var otherPlanner = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 5, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        otherPlanner.MarkAgentDispatched(Now);
        otherPlanner.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now);
        var otherProposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, otherPlanner.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Other plan.", ProposalJson, CollaborationMessageProvenance.ProviderObserved, Now.AddSeconds(1));
        var reviewInputProposal = corruption == ResolverLineageCorruption.ReviewOfDifferentProposal
            ? otherProposal
            : originalProposal;
        var challengeOne = CreateChallenge(
            run.Id, review.Id, reviewInputProposal.Id, Now.AddSeconds(2), ResolverLineageCorruption.SimulatedChallenge == corruption,
            ResolverLineageCorruption.WrongChallengeActor == corruption);
        var otherReview = corruption == ResolverLineageCorruption.ChallengeFromOtherReviewAttempt
            ? Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), run.Id, 6, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now)
            : null;
        otherReview?.MarkAgentDispatched(Now);
        otherReview?.CompleteAgent(AgentOutcome.Challenged, Fingerprint, Now);
        var challengeTwo = CreateChallenge(
            otherRun?.Id ?? run.Id,
            otherReview?.Id ?? review.Id,
            reviewInputProposal.Id, Now.AddSeconds(3), false, false);

        var resolver = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 3, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        resolver.MarkAgentDispatched(Now);
        resolver.CompleteAgent(AgentOutcome.Resolved, Fingerprint, Now);

        var orderedChallenges = corruption == ResolverLineageCorruption.ReorderedChallengeSet
            ? new[] { challengeTwo, challengeOne }
            : new[] { challengeOne, challengeTwo };
        var inputChallenges = corruption switch
        {
            ResolverLineageCorruption.IncompleteChallengeSet => new[] { challengeOne },
            _ => orderedChallenges,
        };
        context.AttemptInputMessages.AddRange(
            new[]
            {
                AttemptInputMessage.Record(Guid.NewGuid(), review.Id, reviewInputProposal.Id, 0),
                AttemptInputMessage.Record(Guid.NewGuid(), resolver.Id, originalProposal.Id, 0),
            }
                .Concat(inputChallenges.Select((challenge, index) => AttemptInputMessage.Record(
                    Guid.NewGuid(), resolver.Id, challenge.Id, index + 1))));

        var decisionReplies = corruption == ResolverLineageCorruption.DuplicatedChallengeSet
            ? new[] { challengeOne, challengeOne }
            : orderedChallenges;
        var decisions = decisionReplies.Select((challenge, index) => CollaborationMessage.RecordAgent(
            resolver, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Decision, challenge.Id, $"Decision {index + 1}.", DecisionJson, Now.AddSeconds(4 + index))).ToArray();
        var revisedProposal = CollaborationMessage.RecordAgent(
            resolver, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, originalProposal.Id, "Revised plan.", ProposalJson, Now.AddSeconds(6));

        var implementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 4, workspace.Id, startingCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        implementation.MarkAgentDispatched(Now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now);
        var executionReport = CollaborationMessage.RecordAgent(
            implementation, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.ExecutionReport, revisedProposal.Id, "Implemented the revised plan.",
            ExecutionReportJson, Now.AddSeconds(7));

        context.Projects.Add(project);
        context.Runs.Add(run);
        if (otherRun is not null)
        {
            context.Runs.Add(otherRun);
        }
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.AddRange(startingCheckpoint, resultCheckpoint);
        if (reviewCheckpoint.Id != startingCheckpoint.Id)
        {
            context.GitCheckpoints.Add(reviewCheckpoint);
        }

        context.Attempts.AddRange(planner, review, resolver, implementation, otherPlanner);
        if (otherReview is not null)
        {
            context.Attempts.Add(otherReview);
        }
        context.CollaborationMessages.AddRange(
            new[] { originalProposal, otherProposal, challengeOne, challengeTwo }.Concat(decisions));
        context.CollaborationMessages.Add(revisedProposal);
        context.CollaborationMessages.Add(executionReport);
        context.AttemptInputMessages.AddRange(
            new[]
            {
                AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, revisedProposal.Id, 0),
            }
                .Concat(decisions.Select((decision, index) => AttemptInputMessage.Record(
                    Guid.NewGuid(), implementation.Id, decision.Id, index + 1))));

        return new Seed(
            run.Id, workspace.Id, resultCheckpoint.Id, originalProposal.Id, challengeOne.Id, decisions[0].Id,
            revisedProposal.Id, executionReport);
    }

    private static async Task MutateResolverSequenceAsync(
        DevalCopilotDbContext context, Seed seed, ResolverLineageCorruption corruption)
    {
        var messageId = corruption switch
        {
            ResolverLineageCorruption.ExecutionReportBeforeProposal => seed.ExecutionReport.Id,
            ResolverLineageCorruption.ChallengeBeforeOriginalProposal => seed.ChallengeOneId,
            ResolverLineageCorruption.DecisionBeforeChallenge => seed.DecisionOneId,
            ResolverLineageCorruption.RevisedProposalBeforeDecision => seed.RevisedProposalId,
            _ => Guid.Empty,
        };
        if (messageId != Guid.Empty)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE collaboration_messages SET Sequence = {-1L} WHERE Id = {messageId}", CancellationToken.None);
        }
    }

    private static CollaborationMessage CreateChallenge(
        Guid runId,
        Guid attemptId,
        Guid proposalId,
        DateTimeOffset occurredAtUtc,
        bool simulated,
        bool wrongActor)
    {
        var actor = wrongActor
            ? ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex)
            : ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode);
        return CollaborationMessage.Record(
            Guid.NewGuid(), runId, attemptId, CollaborationMessage.ProtocolVersionOne, actor,
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Challenge,
            proposalId, "Challenge the plan.", ChallengeJson,
            simulated ? CollaborationMessageProvenance.Simulated : CollaborationMessageProvenance.ProviderObserved,
            occurredAtUtc);
    }

    private sealed record Seed(
        Guid RunId,
        Guid WorkspaceId,
        Guid ResultCheckpointId,
        Guid OriginalProposalId,
        Guid ChallengeOneId,
        Guid DecisionOneId,
        Guid RevisedProposalId,
        CollaborationMessage ExecutionReport);

    public enum ResolverLineageCorruption
    {
        None,
        SimulatedOriginalProposal,
        WrongOriginalProposalActor,
        ChallengeFromOtherRun,
        ChallengeFromOtherReviewAttempt,
        SimulatedChallenge,
        WrongChallengeActor,
        ReviewOfDifferentProposal,
        IncompleteChallengeSet,
        DuplicatedChallengeSet,
        ReorderedChallengeSet,
        StaleReviewCheckpoint,
        ExecutionReportBeforeProposal,
        ChallengeBeforeOriginalProposal,
        DecisionBeforeChallenge,
        RevisedProposalBeforeDecision,
    }

    private const string ProposalJson =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Apply the plan\",\"risks\":\"None\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None\"}";

    private const string ChallengeJson =
        "{\"disputedItem\":\"The plan\",\"materialImpact\":\"It may fail\",\"reasoning\":\"The evidence is incomplete\",\"alternativeOrQuestion\":\"Can it be corrected?\"}";

    private const string DecisionJson =
        "{\"resolution\":\"Addressed\",\"rationale\":\"The concern is resolved\",\"resultingPlanChanges\":\"No additional changes\",\"nextAction\":\"Implement the revised plan\"}";

    private const string ExecutionReportJson =
        "{\"completedWork\":\"Applied the plan\",\"verification\":\"Tests passed\"}";
}
