using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The dispatch boundary of ADR-0015 protects a diagnosis-origin correction exactly as it protects an ordinary one (ADR-0019): the
/// eligibility feed projects only a well-formed snapshot beside the complete coherent version 2 tuple, and
/// <c>MarkAgentAttemptDispatched</c> refuses a snapshot that disagrees with what the supervisor expected, a recorded snapshot with no
/// expectation, and corrupt storage, before any provider can run. Corruption is written with raw SQL beside healthy siblings.
/// </summary>
public sealed class DiagnosisCorrectionDirectGuidanceDispatchTests : IAsyncLifetime
{
    private const string Guidance = "Keep the fix inside the existing helper.";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(DiagnosisTestScene Scene, Guid AttemptId)> GuidedAttemptAsync(string? guidance = Guidance)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        var claim = await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: guidance);
        return (scene, Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(claim.Value).AttemptId);
    }

    private async Task<IReadOnlyList<EligibleReviewCorrectionAttempt>> FeedAsync()
    {
        await using var context = _fixture.CreateContext();
        return await new GetEligibleReviewCorrectionAttemptsQueryHandler(context)
            .HandleAsync(new GetEligibleReviewCorrectionAttemptsQuery(), CancellationToken.None);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task SetRawGuidanceAsync(Guid attemptId, string? value)
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentDirectHumanGuidance = {value} WHERE Id = {attemptId}");
    }

    private async Task<Devalente.Shared.Results.Result<DateTimeOffset>> DispatchAsync(
        DiagnosisTestScene scene, Guid attemptId, ExpectedDirectHumanGuidance? expected)
    {
        await using var context = _fixture.CreateContext();
        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(scene.Run.Id, attemptId, expected), CancellationToken.None);
        if (result.IsSuccess)
        {
            // The handler stages the dispatch; the command pipeline commits it.
            await context.SaveChangesAsync(CancellationToken.None);
        }

        return result;
    }

    private async Task AssertNotDispatchedAsync(Guid attemptId)
    {
        await using var verify = _fixture.CreateContext();
        Assert.Null((await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task The_feed_projects_the_claimed_text_and_the_findings_in_order_and_dispatch_succeeds_with_the_matching_expectation()
    {
        var (scene, attemptId) = await GuidedAttemptAsync();

        var item = Assert.Single(await FeedAsync());
        var result = await DispatchAsync(scene, attemptId, new ExpectedDirectHumanGuidance(item.DirectHumanGuidance!));

        Assert.Equal(attemptId, item.AttemptId);
        Assert.Equal(Guidance, item.DirectHumanGuidance);
        Assert.Equal("claude-review-correction-v2", item.AdapterContractVersion);
        Assert.Equal(2, item.OrderedInputMessageIds.Count);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        await using var verify = _fixture.CreateContext();
        Assert.NotNull((await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task An_unguided_correction_is_projected_without_text_and_dispatches_without_an_expectation()
    {
        var (scene, attemptId) = await GuidedAttemptAsync(guidance: null);

        var item = Assert.Single(await FeedAsync());
        var result = await DispatchAsync(scene, attemptId, null);

        Assert.Null(item.DirectHumanGuidance);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
    }

    [Fact]
    public async Task A_snapshot_that_disagrees_with_the_expectation_is_refused_before_dispatch()
    {
        var (scene, attemptId) = await GuidedAttemptAsync();

        var different = await DispatchAsync(scene, attemptId, new ExpectedDirectHumanGuidance("Some other guidance."));
        var missingExpectation = await DispatchAsync(scene, attemptId, null);

        Assert.Equal("agent_attempts.direct_guidance_mismatch", Assert.Single(different.Errors).Code);
        Assert.Equal("agent_attempts.direct_guidance_mismatch", Assert.Single(missingExpectation.Errors).Code);
        await AssertNotDispatchedAsync(attemptId);
    }

    [Fact]
    public async Task An_expectation_for_an_attempt_that_recorded_no_guidance_is_refused_before_dispatch()
    {
        var (scene, attemptId) = await GuidedAttemptAsync(guidance: null);

        var result = await DispatchAsync(scene, attemptId, new ExpectedDirectHumanGuidance(Guidance));

        Assert.Equal("agent_attempts.direct_guidance_mismatch", Assert.Single(result.Errors).Code);
        await AssertNotDispatchedAsync(attemptId);
    }

    [Fact]
    public async Task A_snapshot_altered_between_the_feed_and_the_dispatch_commit_is_refused_by_the_fresh_read()
    {
        var (scene, attemptId) = await GuidedAttemptAsync();
        var projected = Assert.Single(await FeedAsync()).DirectHumanGuidance!;
        await SetRawGuidanceAsync(attemptId, "Altered after the projection.");

        var result = await DispatchAsync(scene, attemptId, new ExpectedDirectHumanGuidance(projected));

        Assert.Equal("agent_attempts.direct_guidance_mismatch", Assert.Single(result.Errors).Code);
        await AssertNotDispatchedAsync(attemptId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" padded ")]
    [InlineData("café")]
    [InlineData("line\r\nbreak")]
    [InlineData("tab\there")]
    [InlineData("the password is x")]
    public async Task Malformed_stored_text_is_never_eligible_or_dispatchable_and_is_preserved(string stored)
    {
        var (scene, attemptId) = await GuidedAttemptAsync();
        await SetRawGuidanceAsync(attemptId, stored);

        Assert.Empty(await FeedAsync());
        var result = await DispatchAsync(scene, attemptId, new ExpectedDirectHumanGuidance(stored));

        Assert.True(result.IsFailure);
        await AssertNotDispatchedAsync(attemptId);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(
            stored,
            await verify.Database.SqlQuery<string?>($"SELECT AgentDirectHumanGuidance AS Value FROM attempts WHERE Id = {attemptId}").SingleAsync());
    }

    [Theory]
    [InlineData("UPDATE attempts SET AgentProvider = 'Codex'")]
    [InlineData("UPDATE attempts SET AgentPermissionProfile = 'ReadOnly'")]
    [InlineData("UPDATE attempts SET AgentPermissionProfile = NULL")]
    [InlineData("UPDATE attempts SET AgentAdapterContractVersion = NULL")]
    [InlineData("UPDATE attempts SET AgentAdapterContractVersion = 'claude-review-correction-v1'")]
    [InlineData("UPDATE attempts SET AgentAdapterContractVersion = 'claude-implementation-v2'")]
    public async Task Guidance_beside_incoherent_persisted_provenance_is_never_eligible_or_dispatchable(string tamperSql)
    {
        var (scene, attemptId) = await GuidedAttemptAsync();
        await ExecuteAsync(tamperSql);

        Assert.Empty(await FeedAsync());
        var result = await DispatchAsync(scene, attemptId, new ExpectedDirectHumanGuidance(Guidance));

        Assert.True(result.IsFailure);
        await AssertNotDispatchedAsync(attemptId);
    }

    [Fact]
    public async Task A_malformed_guided_correction_never_breaks_a_healthy_sibling_in_the_feed()
    {
        var (_, malformed) = await GuidedAttemptAsync();
        await SetRawGuidanceAsync(malformed, " padded ");
        var (_, healthy) = await GuidedAttemptAsync("Healthy sibling guidance.");

        var feed = await FeedAsync();

        var item = Assert.Single(feed);
        Assert.Equal(healthy, item.AttemptId);
        Assert.Equal("Healthy sibling guidance.", item.DirectHumanGuidance);
    }

    [Fact]
    public async Task Drift_of_the_verification_evidence_after_a_guided_claim_is_still_refused_before_any_provider()
    {
        var (scene, attemptId) = await GuidedAttemptAsync();
        await scene.AddExecutionAsync(1, Spec.Passed());

        var result = await DispatchAsync(scene, attemptId, new ExpectedDirectHumanGuidance(Guidance));

        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        await AssertNotDispatchedAsync(attemptId);
    }
}
