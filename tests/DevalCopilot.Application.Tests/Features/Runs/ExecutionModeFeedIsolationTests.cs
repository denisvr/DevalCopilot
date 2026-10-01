using DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleChallengeResolutionAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleClaudeCriticalReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleCodeReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleProcessAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleSimulatedRuns;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Every eligibility feed hands a supervisor only work whose run mode admits it. Each test owns a fresh
/// database because the feeds scan every attempt with no per-run scoping.</summary>
public sealed class ExecutionModeFeedIsolationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private const int Legacy = (int)RunExecutionMode.Legacy;
    private const int Simulated = (int)RunExecutionMode.Simulated;
    private const int ManualAgent = (int)RunExecutionMode.ManualAgent;
    private const int Undefined = RunExecutionModeTestSupport.UndefinedMode;

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    public static TheoryData<string> AgentFeeds() =>
        ["planning", "critical-review", "challenge-resolution", "implementation", "code-review", "review-correction"];

    private Task<(Guid RunId, Guid AttemptId)> SeedClaimedAgentAttemptAsync(string feed, int storedMode) =>
        AgentAttemptModeSeed.SeedClaimedAsync(_fixture, feed, storedMode);

    private async Task<IReadOnlyList<Guid>> FeedAsync(string feed)
    {
        await using var dbContext = _fixture.CreateContext();
        var cancellationToken = CancellationToken.None;
        return feed switch
        {
            "planning" => (await new GetEligibleAgentAttemptsQueryHandler(dbContext)
                .HandleAsync(new GetEligibleAgentAttemptsQuery(), cancellationToken)).Select(a => a.AttemptId).ToArray(),
            "critical-review" => (await new GetEligibleClaudeCriticalReviewAttemptsQueryHandler(dbContext)
                .HandleAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), cancellationToken)).Select(a => a.AttemptId).ToArray(),
            "challenge-resolution" => (await new GetEligibleChallengeResolutionAttemptsQueryHandler(dbContext)
                .HandleAsync(new GetEligibleChallengeResolutionAttemptsQuery(), cancellationToken)).Select(a => a.AttemptId).ToArray(),
            "implementation" => (await new GetEligibleImplementationAttemptsQueryHandler(dbContext)
                .HandleAsync(new GetEligibleImplementationAttemptsQuery(), cancellationToken)).Select(a => a.AttemptId).ToArray(),
            "code-review" => (await new GetEligibleCodeReviewAttemptsQueryHandler(dbContext)
                .HandleAsync(new GetEligibleCodeReviewAttemptsQuery(), cancellationToken)).Select(a => a.AttemptId).ToArray(),
            "review-correction" => (await new GetEligibleReviewCorrectionAttemptsQueryHandler(dbContext)
                .HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), cancellationToken)).Select(a => a.AttemptId).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(feed)),
        };
    }

    [Theory]
    [MemberData(nameof(AgentFeeds))]
    public async Task Each_agent_feed_hands_out_only_manual_and_legacy_runs(string feed)
    {
        var manual = await SeedClaimedAgentAttemptAsync(feed, ManualAgent);
        var legacy = await SeedClaimedAgentAttemptAsync(feed, Legacy);
        var simulated = await SeedClaimedAgentAttemptAsync(feed, Simulated);
        var undefined = await SeedClaimedAgentAttemptAsync(feed, Undefined);

        var eligible = await FeedAsync(feed);

        Assert.Contains(manual.AttemptId, eligible);
        Assert.Contains(legacy.AttemptId, eligible);
        Assert.DoesNotContain(simulated.AttemptId, eligible);
        Assert.DoesNotContain(undefined.AttemptId, eligible);
    }

    [Theory]
    [MemberData(nameof(AgentFeeds))]
    public async Task A_mode_change_after_the_claim_removes_the_attempt_from_its_feed(string feed)
    {
        var claimed = await SeedClaimedAgentAttemptAsync(feed, ManualAgent);
        Assert.Contains(claimed.AttemptId, await FeedAsync(feed));

        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, claimed.RunId, Simulated);

        Assert.DoesNotContain(claimed.AttemptId, await FeedAsync(feed));
    }

    [Theory]
    [InlineData(Simulated, true)]
    [InlineData(Legacy, true)]
    [InlineData(ManualAgent, false)]
    [InlineData(Undefined, false)]
    public async Task The_simulation_feed_hands_out_only_simulated_and_legacy_created_runs(int storedMode, bool eligible)
    {
        Guid runId;
        await using (var dbContext = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
            dbContext.Projects.Add(project);
            dbContext.Runs.Add(run);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await RunExecutionModeTestSupport.SetStoredModeAsync(dbContext, run.Id, storedMode);
            runId = run.Id;
        }

        await using var query = _fixture.CreateContext();
        var feed = await new GetEligibleSimulatedRunsQueryHandler(query)
            .HandleAsync(new GetEligibleSimulatedRunsQuery(), CancellationToken.None);

        Assert.Equal(eligible, feed.Contains(runId));
    }

    [Theory]
    [InlineData(Legacy, true)]
    [InlineData(Simulated, false)]
    [InlineData(ManualAgent, false)]
    [InlineData(Undefined, false)]
    public async Task The_process_feed_hands_out_only_running_legacy_runs(int storedMode, bool eligible)
    {
        Guid attemptId;
        await using (var dbContext = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
            run.Claim(Now);
            var intent = new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\x", @"C:\repos", TimeSpan.FromMinutes(1), 1024, 2048);
            var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, intent, Now);
            dbContext.Projects.Add(project);
            dbContext.Runs.Add(run);
            dbContext.Attempts.Add(attempt);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await RunExecutionModeTestSupport.SetStoredModeAsync(dbContext, run.Id, storedMode);
            attemptId = attempt.Id;
        }

        await using var query = _fixture.CreateContext();
        var feed = await new GetEligibleProcessAttemptsQueryHandler(query)
            .HandleAsync(new GetEligibleProcessAttemptsQuery(), CancellationToken.None);

        Assert.Equal(eligible, feed.Any(candidate => candidate.AttemptId == attemptId));
    }
}
