using DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleChallengeResolutionAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleCodeReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleVerificationDiagnosisAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The four Codex eligibility feeds each hand their supervisor only the attempts of their own role and response contract
/// (ADR-0009). Every supervisor invokes exactly one adapter and parses exactly one contract, so a feed that also returned a
/// sibling's attempt would let the wrong supervisor dispatch and misjudge it. Every attempt below sits in its own
/// otherwise-eligible run, workspace, lease, checkpoint, and manifest, so the role and contract are the only difference;
/// persisted incoherent, null, and unknown values are written with raw SQL to prove they enter no feed and break none.
/// </summary>
public sealed class CodexAgentFeedRoutingTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();
    private int _leaseSequence;

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed record Claim(Guid AttemptId, Guid RunId, Guid WorkspaceId, Guid CheckpointId, Guid ManifestId);

    private async Task<Guid> SeedAsync(
        Func<Claim, Guid?, Attempt> create, Func<Claim, Attempt>? createSource = null, bool withResolutionInputs = false)
    {
        await using var db = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Route the next stage", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var claim = new Claim(Guid.NewGuid(), run.Id, workspace.Id, Guid.NewGuid(), Guid.NewGuid());
        Attempt? source = null;
        if (createSource is not null)
        {
            // A format repair names a real, failed earlier attempt of the same contract as its source.
            var sourceClaim = new Claim(Guid.NewGuid(), run.Id, workspace.Id, claim.CheckpointId, Guid.NewGuid());
            source = createSource(sourceClaim);
            source.MarkAgentDispatched(Now);
            source.CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, Now, TestProcessEvidence.CleanExit);
        }

        var attempt = create(claim, source?.Id);

        db.Projects.Add(project);
        db.Runs.Add(run);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(GitCheckpoint.Capture(claim.CheckpointId, workspace.Id, 1, Now, new string('a', 40), Fingerprint, []));
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, (ulong)++_leaseSequence, BitConverter.GetBytes(_leaseSequence).Concat(new byte[12]).ToArray(), Now));
        if (source is not null)
        {
            db.Attempts.Add(source);
            db.Artifacts.Add(Artifact.Record(
                source.AgentContextManifestArtifactId!.Value, run.Id, source.Id, ArtifactPurpose.AgentContextManifest, "application/json",
                "runs/source/manifest.sealed", "sha256:source", 256, false,
                ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
            await db.SaveChangesAsync();
        }

        db.Attempts.Add(attempt);
        db.Artifacts.Add(Artifact.Record(
            claim.ManifestId, run.Id, attempt.Id, ArtifactPurpose.AgentContextManifest, "application/json",
            @"runs\r\attempts\a\manifest.sealed", "sha256:manifest", 256, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        if (withResolutionInputs)
        {
            db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), sequence: 0));
            db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), sequence: 1));
        }

        await db.SaveChangesAsync();
        return attempt.Id;
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private static Attempt Planning(Claim c, int number = 1) => Attempt.ClaimAgent(
        c.AttemptId, c.RunId, number, c.WorkspaceId, c.CheckpointId, Fingerprint, c.ManifestId, Timeout, 262144, 524288, Now, number);

    private static Attempt Resolver(Claim c) => Attempt.ClaimAgentChallengeResolution(
        c.AttemptId, c.RunId, 1, c.WorkspaceId, c.CheckpointId, Fingerprint, c.ManifestId, Timeout, 262144, 524288, Now, 1);

    private static Attempt Review(Claim c) => Attempt.ClaimAgentCodeReview(
        c.AttemptId, c.RunId, 1, c.WorkspaceId, c.CheckpointId, Fingerprint, c.ManifestId, Timeout, 262144, 524288, Now, 1);

    private Task<Guid> PlanningAsync() => SeedAsync((c, _) => Planning(c));

    private Task<Guid> PlanningRepairAsync() => SeedAsync(
        (c, source) => Attempt.ClaimAgentPlanningRepair(
            c.AttemptId, c.RunId, 2, c.WorkspaceId, c.CheckpointId, Fingerprint, c.ManifestId, Timeout, 262144, 524288, Now,
            requestedModel: null, requestedEffort: null, agentBudgetSlot: 2, repairSourceAttemptId: source!.Value),
        c => Planning(c));

    private Task<Guid> ResolverAsync() => SeedAsync((c, _) => Resolver(c), withResolutionInputs: true);

    private Task<Guid> ResolverRepairAsync() => SeedAsync(
        (c, source) => Attempt.ClaimAgentChallengeResolutionWithAssignment(
            c.AttemptId, c.RunId, 2, c.WorkspaceId, c.CheckpointId, Fingerprint, c.ManifestId, Timeout, 262144, 524288, Now,
            requestedModel: null, requestedEffort: null, agentBudgetSlot: 2, repairSourceAttemptId: source!.Value),
        Resolver,
        withResolutionInputs: true);

    private Task<Guid> ReviewAsync() => SeedAsync((c, _) => Review(c));

    private Task<Guid> ReviewRepairAsync() => SeedAsync(
        (c, source) => Attempt.ClaimAgentCodeReviewWithAssignment(
            c.AttemptId, c.RunId, 2, c.WorkspaceId, c.CheckpointId, Fingerprint, c.ManifestId, Timeout, 262144, 524288, Now,
            requestedModel: null, requestedEffort: null, agentBudgetSlot: 2, repairSourceAttemptId: source!.Value),
        Review);

    private Task<Guid> DiagnosisAsync() => SeedAsync((c, _) => Attempt.ClaimAgentVerificationDiagnosis(
        c.AttemptId, c.RunId, 1, c.WorkspaceId, c.CheckpointId, Fingerprint, c.ManifestId, Timeout, 262144, 524288, Now,
        requestedModel: null, requestedEffort: null, agentBudgetSlot: 1));

    /// <summary>An otherwise-eligible planning attempt whose persisted role and contract are then overwritten, as a corrupted
    /// or historical row would be. SQL NULL and arbitrary text are both written directly.</summary>
    private async Task<Guid> CorruptedAsync(string? role, string? contract)
    {
        var id = await PlanningAsync();
        await using var db = _fixture.CreateContext();
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE attempts SET AgentRole = {0}, AgentResponseContract = {1} WHERE Id = {2}", role!, contract!, id.ToString().ToUpperInvariant());
#pragma warning restore EF1002
        return id;
    }

    private async Task<IReadOnlyList<Guid>> PlanningFeedAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await new GetEligibleAgentAttemptsQueryHandler(db).HandleAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None))
            .Select(a => a.AttemptId).ToArray();
    }

    private async Task<IReadOnlyList<Guid>> ResolverFeedAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await new GetEligibleChallengeResolutionAttemptsQueryHandler(db)
                .HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), CancellationToken.None))
            .Select(a => a.AttemptId).ToArray();
    }

    private async Task<IReadOnlyList<Guid>> ReviewFeedAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await new GetEligibleCodeReviewAttemptsQueryHandler(db).HandleAsync(new GetEligibleCodeReviewAttemptsQuery(), CancellationToken.None))
            .Select(a => a.AttemptId).ToArray();
    }

    private async Task<IReadOnlyList<Guid>> DiagnosisFeedAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await new GetEligibleVerificationDiagnosisAttemptsQueryHandler(db)
                .HandleAsync(new GetEligibleVerificationDiagnosisAttemptsQuery(), CancellationToken.None))
            .Select(a => a.AttemptId).ToArray();
    }

    private static void AssertSame(IEnumerable<Guid> expected, IReadOnlyList<Guid> actual) =>
        Assert.Equal(expected.OrderBy(id => id), actual.OrderBy(id => id));

    [Fact]
    public async Task The_planning_feed_returns_ordinary_and_format_repair_planning_but_never_a_sibling_codex_role()
    {
        var planning = await PlanningAsync();
        var repair = await PlanningRepairAsync();
        await ResolverAsync();
        await ResolverRepairAsync();
        await ReviewAsync();
        await ReviewRepairAsync();
        await DiagnosisAsync();

        AssertSame([planning, repair], await PlanningFeedAsync());
    }

    [Fact]
    public async Task The_resolver_feed_requires_its_exact_role_and_response_contract()
    {
        var resolver = await ResolverAsync();
        var repair = await ResolverRepairAsync();
        await PlanningAsync();
        await PlanningRepairAsync();
        await ReviewAsync();
        await DiagnosisAsync();
        // A Resolver whose persisted contract is another Codex contract is incoherent and must not be served.
        var incoherent = await ResolverAsync();
        await using (var db = _fixture.CreateContext())
        {
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE attempts SET AgentResponseContract = 'Proposal' WHERE Id = {0}", incoherent.ToString().ToUpperInvariant());
#pragma warning restore EF1002
        }

        AssertSame([resolver, repair], await ResolverFeedAsync());
    }

    [Fact]
    public async Task Each_of_the_four_codex_feeds_selects_exactly_its_own_partition_when_all_families_are_present()
    {
        var planning = await PlanningAsync();
        var planningRepair = await PlanningRepairAsync();
        var resolver = await ResolverAsync();
        var resolverRepair = await ResolverRepairAsync();
        var review = await ReviewAsync();
        var reviewRepair = await ReviewRepairAsync();
        var diagnosis = await DiagnosisAsync();

        AssertSame([planning, planningRepair], await PlanningFeedAsync());
        AssertSame([resolver, resolverRepair], await ResolverFeedAsync());
        AssertSame([review, reviewRepair], await ReviewFeedAsync());
        AssertSame([diagnosis], await DiagnosisFeedAsync());
    }

    [Theory]
    [InlineData("Resolver", "Proposal")]
    [InlineData("Planner", "ChallengeResolution")]
    [InlineData("CodeReviewer", "Proposal")]
    [InlineData("Planner", "VerificationDiagnosis")]
    [InlineData("Planner", null)]
    [InlineData(null, "Proposal")]
    [InlineData(null, null)]
    [InlineData("NotARole", "NotAContract")]
    [InlineData("Planner", "NotAContract")]
    public async Task Incoherent_null_and_unknown_role_and_contract_values_enter_no_codex_feed_and_break_no_healthy_sibling(
        string? role, string? contract)
    {
        var healthyPlanning = await PlanningAsync();
        var healthyResolver = await ResolverAsync();
        var healthyReview = await ReviewAsync();
        var healthyDiagnosis = await DiagnosisAsync();
        var corrupted = await CorruptedAsync(role, contract);

        var planningFeed = await PlanningFeedAsync();
        var resolverFeed = await ResolverFeedAsync();
        var reviewFeed = await ReviewFeedAsync();
        var diagnosisFeed = await DiagnosisFeedAsync();

        // A row that is a genuine, coherent planning attempt (Planner + Proposal) is the only corrupted shape a feed may serve.
        var coherentPlanning = role == "Planner" && contract == "Proposal";
        AssertSame(coherentPlanning ? [healthyPlanning, corrupted] : [healthyPlanning], planningFeed);
        AssertSame([healthyResolver], resolverFeed);
        AssertSame([healthyReview], reviewFeed);
        AssertSame([healthyDiagnosis], diagnosisFeed);
        Assert.DoesNotContain(corrupted, resolverFeed);
        Assert.DoesNotContain(corrupted, reviewFeed);
        Assert.DoesNotContain(corrupted, diagnosisFeed);
    }
}
