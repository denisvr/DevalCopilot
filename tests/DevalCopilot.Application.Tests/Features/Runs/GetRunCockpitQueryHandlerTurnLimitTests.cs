using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The cockpit reports the run's current saved turn-limit request and the latest Agent
/// attempt's own immutable turn-limit record as two distinct facts.</summary>
public sealed class GetRunCockpitQueryHandlerTurnLimitTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> SeedRunAsync(Func<Guid, Attempt>? attemptFactory = null)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Cockpit turn limit", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Inspect the cockpit", Now);
        context.Projects.Add(project);
        context.Runs.Add(run);
        if (attemptFactory is not null)
        {
            context.Attempts.Add(attemptFactory(run.Id));
        }

        await context.SaveChangesAsync();
        return run.Id;
    }

    private static Attempt Implementation(Guid runId, int? limit) => Attempt.ClaimAgentImplementationWithAssignment(
        Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(20), 65536, 131072, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
        ClaudeMutationAdapterContract.ImplementationV2, 1, limit);

    private async Task<GetRunCockpitQueryResult> ReadAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var result = await new GetRunCockpitQueryHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new GetRunCockpitQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public async Task A_run_without_a_request_or_attempt_reports_not_requested_and_no_attempt_fact()
    {
        var runId = await SeedRunAsync();

        var cockpit = await ReadAsync(runId);

        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.NotRequested, null), cockpit.ClaudeTurnLimitRequest);
        Assert.Null(cockpit.LatestAgentAttempt);
    }

    [Fact]
    public async Task The_run_request_is_reported_independently_of_the_latest_attempts_record()
    {
        var runId = await SeedRunAsync(id => Implementation(id, 7));
        await ClaudeMutationTurnLimitTestSupport.SetLimitAsync(_fixture, runId, 60);

        var cockpit = await ReadAsync(runId);

        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Requested, 60), cockpit.ClaudeTurnLimitRequest);
        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Requested, 7), cockpit.LatestAgentAttempt!.TurnLimit);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task The_latest_v2_attempt_reports_its_requested_limit(int limit)
    {
        var runId = await SeedRunAsync(id => Implementation(id, limit));

        var cockpit = await ReadAsync(runId);

        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Requested, limit), cockpit.LatestAgentAttempt!.TurnLimit);
        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.NotRequested, null), cockpit.ClaudeTurnLimitRequest);
    }

    [Fact]
    public async Task The_latest_v2_attempt_without_a_limit_reports_not_requested()
    {
        var runId = await SeedRunAsync(id => Implementation(id, null));

        var cockpit = await ReadAsync(runId);

        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.NotRequested, null), cockpit.LatestAgentAttempt!.TurnLimit);
    }

    [Fact]
    public async Task A_legacy_v1_attempt_reports_not_recorded()
    {
        var runId = await SeedRunAsync(id => Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, Now, 1));

        var cockpit = await ReadAsync(runId);

        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.NotRecorded, null), cockpit.LatestAgentAttempt!.TurnLimit);
    }

    [Fact]
    public async Task A_v2_review_correction_attempt_reports_its_requested_limit()
    {
        var runId = await SeedRunAsync(id => Attempt.ClaimAgentReviewCorrectionWithModelRequest(
            Guid.NewGuid(), id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, Now, null, null, 1, 12));

        var cockpit = await ReadAsync(runId);

        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Requested, 12), cockpit.LatestAgentAttempt!.TurnLimit);
    }

    [Fact]
    public async Task A_non_mutation_attempt_has_no_turn_limit_fact()
    {
        var runId = await SeedRunAsync(id => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, Now, 1));

        var cockpit = await ReadAsync(runId);

        Assert.NotNull(cockpit.LatestAgentAttempt);
        Assert.Null(cockpit.LatestAgentAttempt.TurnLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task An_out_of_range_run_value_is_reported_as_unknown_and_never_clamped(int stored)
    {
        var runId = await SeedRunAsync(id => Implementation(id, 5));
        await ClaudeMutationTurnLimitTestSupport.SetRawRunLimitAsync(_fixture, runId, stored);

        var cockpit = await ReadAsync(runId);

        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Unknown, null), cockpit.ClaudeTurnLimitRequest);
        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Requested, 5), cockpit.LatestAgentAttempt!.TurnLimit);
    }

    [Fact]
    public async Task A_malformed_attempt_value_is_reported_as_unknown_and_does_not_fail_the_cockpit()
    {
        var runId = await SeedRunAsync(id => Implementation(id, 5));
        Guid attemptId;
        await using (var context = _fixture.CreateContext())
        {
            attemptId = context.Attempts.Single(a => a.RunId == runId).Id;
        }

        await ClaudeMutationTurnLimitTestSupport.SetRawAttemptLimitAsync(_fixture, attemptId, 0);

        var cockpit = await ReadAsync(runId);

        Assert.Equal(new(ClaudeMutationTurnLimitEvidence.Unknown, null), cockpit.LatestAgentAttempt!.TurnLimit);
    }
}
