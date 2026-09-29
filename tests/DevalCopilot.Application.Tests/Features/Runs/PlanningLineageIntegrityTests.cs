using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>SQLite-persisted corruption of the identity the lineage relies on: role/provider pairs
/// DevalCopilot never launches (with a matching forged message actor), undefined roles and response
/// contracts, and unparseable stored enum strings on a participating row or an unrelated row. Every
/// review, resolution, implementation, and result path refuses with a fixed error that echoes neither the
/// stored value nor an exception, and does no external work, seals no manifest, and records nothing.</summary>
public sealed class PlanningLineageIntegrityTests : IAsyncLifetime
{
    private const string Garbage = "ZzUnparseable";

    private static readonly string[] FixedLineageCodes =
    [
        "agent_attempts.proposal_attempt_not_valid",
        "agent_attempts.proposal_lineage_not_valid",
        "agent_attempts.decisions_not_valid",
        "agent_attempts.challenged_review_not_valid",
    ];

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    public enum Corruption
    {
        ForgedPlannerPair,
        ForgedResolverPair,
        ForgedReviewerPair,
        UndefinedResolverRole,
        UndefinedResolverContract,
        UnreadableParticipatingRow,
        UnreadableUnrelatedRow,
    }

    private sealed record Lineage(
        Scene Scene,
        CollaborationMessage Root,
        Guid RootAttemptId,
        PlanningLineageSeeder.Review FirstReview,
        PlanningLineageSeeder.Resolution FirstResolution,
        PlanningLineageSeeder.Review SecondReview,
        Guid UnrelatedAttemptId);

    private async Task<Lineage> SeedAsync(DevalCopilotDbContext dbContext)
    {
        var scene = await SeedSceneAsync(dbContext);
        var seeder = SeederFor(dbContext, scene);
        var (planner, root) = seeder.AddRoot();
        var (firstReview, firstResolution) = seeder.AddChallengedRound(root, challengeCount: 2);
        var secondReview = seeder.AddReview(firstResolution.RevisedProposal, AgentOutcome.Challenged, challengeCount: 2);
        var unrelated = seeder.AddRoot().Planner;
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new Lineage(scene, root, planner.Id, firstReview, firstResolution, secondReview, unrelated.Id);
    }

    private static async Task CorruptAsync(DevalCopilotDbContext dbContext, Lineage lineage, Corruption corruption)
    {
        var resolver = lineage.FirstResolution.Attempt.Id;
        switch (corruption)
        {
            case Corruption.ForgedPlannerPair:
                await ForgeAsync(dbContext, lineage.RootAttemptId, AgentProvider.ClaudeCode);
                break;
            case Corruption.ForgedResolverPair:
                await ForgeAsync(dbContext, resolver, AgentProvider.ClaudeCode);
                break;
            case Corruption.ForgedReviewerPair:
                await ForgeAsync(dbContext, lineage.FirstReview.Attempt.Id, AgentProvider.Codex);
                await ForgeAsync(dbContext, lineage.SecondReview.Attempt.Id, AgentProvider.Codex);
                break;
            case Corruption.UndefinedResolverRole:
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRole = {"99"} WHERE Id = {resolver}");
                break;
            case Corruption.UndefinedResolverContract:
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentResponseContract = {"99"} WHERE Id = {resolver}");
                break;
            case Corruption.UnreadableParticipatingRow:
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET Status = {Garbage} WHERE Id = {resolver}");
                break;
            case Corruption.UnreadableUnrelatedRow:
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentRole = {Garbage} WHERE Id = {lineage.UnrelatedAttemptId}");
                break;
        }
    }

    // Forges an unsupported persisted role/provider pair AND makes the attempt's messages' recorded actor
    // match it, so only the pair rule (not the actor comparison) can refuse it.
    private static async Task ForgeAsync(DevalCopilotDbContext dbContext, Guid ownerAttemptId, AgentProvider provider)
    {
        await dbContext.Attempts.Where(a => a.Id == ownerAttemptId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.AgentProvider, provider));
        await dbContext.CollaborationMessages.Where(m => m.AttemptId == ownerAttemptId)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.ActorAgentProvider, provider));
    }

    private static void AssertFixedRefusal(Devalente.Shared.Results.Error error)
    {
        Assert.Contains(error.Code, FixedLineageCodes);
        Assert.DoesNotContain(Garbage, error.Description);
        Assert.DoesNotContain("99", error.Description);
        Assert.DoesNotContain("Exception", error.Description);
    }

    [Theory]
    [InlineData(Corruption.ForgedPlannerPair)]
    [InlineData(Corruption.ForgedResolverPair)]
    [InlineData(Corruption.ForgedReviewerPair)]
    [InlineData(Corruption.UndefinedResolverRole)]
    [InlineData(Corruption.UndefinedResolverContract)]
    [InlineData(Corruption.UnreadableParticipatingRow)]
    [InlineData(Corruption.UnreadableUnrelatedRow)]
    public async Task Review_and_implementation_claims_refuse_a_corrupt_lineage_before_any_external_work(Corruption corruption)
    {
        await using var seedContext = _fixture.CreateContext();
        var lineage = await SeedAsync(seedContext);
        await CorruptAsync(seedContext, lineage, corruption);
        var attemptsBefore = await seedContext.Attempts.CountAsync(a => a.RunId == lineage.Scene.Run.Id);
        var reviewedProposalId = corruption == Corruption.ForgedPlannerPair ? lineage.Root.Id : lineage.FirstResolution.RevisedProposal.Id;

        await using var dbContext = _fixture.CreateContext();
        var reviewEvidence = new CountingEvidenceReader();
        var reviewStore = new RecordingArtifactStore();
        var review = await new CreateClaudeCriticalReviewAttemptCommandHandler(
                dbContext, reviewEvidence, reviewStore, new FixedTimeProvider(Now))
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(lineage.Scene.Run.Id, reviewedProposalId), CancellationToken.None);

        // The first revision has only its Challenged second review in this seed, so implementation of it
        // is refused for that reason at best; corruption must refuse it first, with a lineage error.
        var implementationEvidence = new CountingEvidenceReader();
        var implementationStore = new RecordingArtifactStore();
        var implementation = await new CreateImplementationAttemptCommandHandler(
                dbContext, implementationEvidence, implementationStore, new FixedTimeProvider(Now))
            .HandleAsync(
                new CreateImplementationAttemptCommand(lineage.Scene.Run.Id, lineage.FirstResolution.RevisedProposal.Id),
                CancellationToken.None);

        Assert.True(review.IsFailure);
        AssertFixedRefusal(Assert.Single(review.Errors));
        Assert.Equal(0, reviewEvidence.Captures);
        Assert.Equal(0, reviewStore.Seals);

        Assert.True(implementation.IsFailure);
        AssertFixedRefusal(Assert.Single(implementation.Errors));
        Assert.Equal(0, implementationEvidence.Captures);
        Assert.Equal(0, implementationStore.Seals);

        await using var verification = _fixture.CreateContext();
        Assert.Equal(attemptsBefore, await verification.Attempts.CountAsync(a => a.RunId == lineage.Scene.Run.Id));
    }

    [Theory]
    [InlineData(Corruption.ForgedResolverPair)]
    [InlineData(Corruption.ForgedReviewerPair)]
    [InlineData(Corruption.UndefinedResolverRole)]
    [InlineData(Corruption.UnreadableParticipatingRow)]
    [InlineData(Corruption.UnreadableUnrelatedRow)]
    public async Task The_resolution_claim_refuses_a_corrupt_lineage_and_seals_no_manifest(Corruption corruption)
    {
        await using var seedContext = _fixture.CreateContext();
        var lineage = await SeedAsync(seedContext);
        await CorruptAsync(seedContext, lineage, corruption);

        await using var dbContext = _fixture.CreateContext();
        var store = new RecordingArtifactStore();
        var result = await new CreateChallengeResolutionAttemptCommandHandler(
                dbContext, new CountingEvidenceReader(), store, new FixedTimeProvider(Now), new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(new CreateChallengeResolutionAttemptCommand(lineage.Scene.Run.Id, lineage.SecondReview.Attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertFixedRefusal(Assert.Single(result.Errors));
        Assert.Equal(0, store.Seals);
        await using var verification = _fixture.CreateContext();
        Assert.Empty(verification.Attempts.Where(a => a.AgentRole == AgentRole.Resolver && a.Status == AttemptStatus.Running));
    }

    [Theory]
    [InlineData(Corruption.ForgedResolverPair)]
    [InlineData(Corruption.ForgedReviewerPair)]
    [InlineData(Corruption.UnreadableParticipatingRow)]
    [InlineData(Corruption.UnreadableUnrelatedRow)]
    public async Task The_resolution_result_refuses_a_corrupt_lineage_and_records_nothing(Corruption corruption)
    {
        await using var seedContext = _fixture.CreateContext();
        var lineage = await SeedAsync(seedContext);
        var number = 40;
        var running = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), lineage.Scene.Run.Id, number, lineage.Scene.Workspace.Id, lineage.Scene.Checkpoint.Id, Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
        running.MarkAgentDispatched(Now);
        seedContext.Attempts.Add(running);
        seedContext.AttemptInputMessages.Add(AttemptInputMessage.Record(
            Guid.NewGuid(), running.Id, lineage.FirstResolution.RevisedProposal.Id, sequence: 0));
        for (var index = 0; index < lineage.SecondReview.Outputs.Count; index++)
        {
            seedContext.AttemptInputMessages.Add(AttemptInputMessage.Record(
                Guid.NewGuid(), running.Id, lineage.SecondReview.Outputs[index].Id, sequence: index + 1));
        }

        await seedContext.SaveChangesAsync(CancellationToken.None);
        await CorruptAsync(seedContext, lineage, corruption);

        await using var dbContext = _fixture.CreateContext();
        var attempt = await dbContext.Attempts.SingleAsync(a => a.Id == running.Id);
        var resolution = ValidatedChallengeResolution.Create(
            "Overall resolution summary",
            lineage.SecondReview.Outputs.Select(challenge => new ValidatedDecision(
                challenge.Id, "Decision summary",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    resolution = DevalCopilot.Application.Features.Runs.ChallengeResolutionOutputSchema.AcceptedResolution,
                    rationale = "Rationale",
                    resultingPlanChanges = "Plan changes",
                    nextAction = "Next action",
                }))).ToList(),
            new ValidatedRevisedProposal("Second revised proposal summary", PlanningLineageSeeder.ProposalJson("Second revised scope")));

        var result = await new RecordChallengeResolutionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordChallengeResolutionResultCommand(
                lineage.Scene.Run.Id, running.Id, AgentOutcome.Resolved, Fingerprint, [], resolution, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertFixedRefusal(Assert.Single(result.Errors));
        await using var verification = _fixture.CreateContext();
        Assert.Empty(verification.CollaborationMessages.Where(m => m.AttemptId == running.Id));
        Assert.Empty(verification.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.Escalation));
        Assert.Equal(AttemptStatus.Running, (await verification.Attempts.SingleAsync(a => a.Id == running.Id)).Status);
    }

    [Fact]
    public async Task A_forged_planner_root_is_also_refused_by_the_accepted_original_implementation_path()
    {
        await using var seedContext = _fixture.CreateContext();
        var scene = await SeedSceneAsync(seedContext);
        var seeder = SeederFor(seedContext, scene);
        var (planner, root) = seeder.AddRoot();
        seeder.AddReview(root, AgentOutcome.Accepted);
        await seedContext.SaveChangesAsync(CancellationToken.None);
        await ForgeAsync(seedContext, planner.Id, AgentProvider.ClaudeCode);

        await using var dbContext = _fixture.CreateContext();
        var store = new RecordingArtifactStore();
        var result = await new CreateImplementationAttemptCommandHandler(
                dbContext, new CountingEvidenceReader(), store, new FixedTimeProvider(Now))
            .HandleAsync(new CreateImplementationAttemptCommand(scene.Run.Id, root.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.proposal_attempt_not_valid", Assert.Single(result.Errors).Code);
        Assert.Equal(0, store.Seals);
    }
}
