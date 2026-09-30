using DevalCopilot.Application.Features.Runs.Commands.SetClaudeMutationTurnLimit;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.Commands;

public sealed class SetClaudeMutationTurnLimitCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> SeedRunAsync(bool claim = false, int? maxTurns = null)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        context.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        if (claim)
        {
            run.Claim(Now);
        }

        if (maxTurns is not null)
        {
            run.SetRequestedClaudeMaxTurns(maxTurns);
        }

        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private static SetClaudeMutationTurnLimitCommandHandler Handler(DevalCopilotDbContext context) =>
        new(context, new FixedTimeProvider(Now));

    private async Task<(int? MaxTurns, IReadOnlyList<RunEvent> Events)> ReadStateAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        var events = await context.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId
                && candidate.EventType == RunEventType.ClaudeMutationTurnLimitChanged)
            .OrderBy(candidate => candidate.Sequence)
            .ToListAsync();
        return (run.RequestedClaudeMaxTurns, events);
    }

    private async Task CompleteRunAsync(Guid runId)
    {
        await using var competing = _fixture.CreateContext();
        var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.Complete(Now.AddMinutes(1));
        await competing.SaveChangesAsync();
    }

    private async Task CompetingSetAsync(Guid runId, int? maxTurns)
    {
        await using var competing = _fixture.CreateContext();
        var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.SetRequestedClaudeMaxTurns(maxTurns);
        await competing.SaveChangesAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(100)]
    public async Task HandleAsync_sets_the_limit_and_records_one_human_event(int maxTurns)
    {
        var runId = await SeedRunAsync(claim: true);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, maxTurns), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(maxTurns, result.Value.MaxTurns);
        var (stored, events) = await ReadStateAsync(runId);
        Assert.Equal(maxTurns, stored);
        var recorded = Assert.Single(events);
        Assert.Equal("{\"maxTurns\":" + maxTurns + "}", recorded.PayloadJson);
        Assert.Equal(RunEventType.ClaudeMutationTurnLimitChanged, recorded.EventType);
        Assert.Equal(ParticipantKind.Human, recorded.ActorKind);
        Assert.Null(recorded.AttemptId);
    }

    [Fact]
    public async Task HandleAsync_sets_the_limit_on_a_created_run()
    {
        var runId = await SeedRunAsync(claim: false);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, 12), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(12, (await ReadStateAsync(runId)).MaxTurns);
    }

    [Fact]
    public async Task HandleAsync_changes_a_stored_limit()
    {
        var runId = await SeedRunAsync(claim: true, maxTurns: 10);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (stored, events) = await ReadStateAsync(runId);
        Assert.Equal(20, stored);
        Assert.Equal("{\"maxTurns\":20}", Assert.Single(events).PayloadJson);
    }

    [Fact]
    public async Task HandleAsync_clears_the_limit_and_records_a_null_event()
    {
        var runId = await SeedRunAsync(claim: true, maxTurns: 10);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.MaxTurns);
        var (stored, events) = await ReadStateAsync(runId);
        Assert.Null(stored);
        Assert.Equal("{\"maxTurns\":null}", Assert.Single(events).PayloadJson);
    }

    [Fact]
    public async Task HandleAsync_same_value_set_still_succeeds_and_appends_its_event()
    {
        var runId = await SeedRunAsync(claim: true, maxTurns: 10);

        for (var repetition = 1; repetition <= 2; repetition++)
        {
            await using var context = _fixture.CreateContext();
            var result = await Handler(context).HandleAsync(
                new SetClaudeMutationTurnLimitCommand(runId, 10), CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(repetition, (await ReadStateAsync(runId)).Events.Count);
        }

        Assert.Equal(10, (await ReadStateAsync(runId)).MaxTurns);
    }

    [Fact]
    public async Task HandleAsync_same_value_clear_of_an_unset_run_still_appends_its_event()
    {
        var runId = await SeedRunAsync(claim: true);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("{\"maxTurns\":null}", Assert.Single((await ReadStateAsync(runId)).Events).PayloadJson);
    }

    [Fact]
    public async Task HandleAsync_returns_not_found_for_a_missing_run_without_any_event()
    {
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(Guid.NewGuid(), 5), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ClaudeMutationTurnLimitErrors.RunNotFoundCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, await context.Events.CountAsync(e => e.EventType == RunEventType.ClaudeMutationTurnLimitChanged));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(null)]
    public async Task HandleAsync_rejects_a_terminal_run_even_for_a_same_value_set_with_no_write(int? requested)
    {
        var runId = await SeedRunAsync(claim: true, maxTurns: 7);
        await CompleteRunAsync(runId);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, requested), CancellationToken.None);

        Assert.Equal(ClaudeMutationTurnLimitErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await ReadStateAsync(runId);
        Assert.Equal(7, stored);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandleAsync_reports_a_lifecycle_change_after_its_read_as_not_editable_with_no_event()
    {
        var runId = await SeedRunAsync(claim: true, maxTurns: 7);
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompleteRunAsync(runId)));

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, 9), CancellationToken.None);

        Assert.Equal(ClaudeMutationTurnLimitErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await ReadStateAsync(runId);
        Assert.Equal(7, stored);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandleAsync_reports_a_lifecycle_change_after_a_same_value_set_and_appends_no_event()
    {
        var runId = await SeedRunAsync(claim: true, maxTurns: 7);
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompleteRunAsync(runId)));

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, 7), CancellationToken.None);

        Assert.Equal(ClaudeMutationTurnLimitErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        Assert.Empty((await ReadStateAsync(runId)).Events);
    }

    [Fact]
    public async Task HandleAsync_reports_a_competing_set_as_a_retryable_conflict_without_partial_write()
    {
        var runId = await SeedRunAsync(claim: true);
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompetingSetAsync(runId, 33)));

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, 5), CancellationToken.None);

        Assert.Equal(ClaudeMutationTurnLimitErrors.ConcurrentChangeCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await ReadStateAsync(runId);
        Assert.Equal(33, stored);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandleAsync_reports_a_competing_clear_as_a_conflict_without_partial_write()
    {
        var runId = await SeedRunAsync(claim: true, maxTurns: 8);
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompetingSetAsync(runId, null)));

        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, 8), CancellationToken.None);

        Assert.Equal(ClaudeMutationTurnLimitErrors.ConcurrentChangeCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await ReadStateAsync(runId);
        Assert.Null(stored);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandleAsync_does_not_touch_an_already_claimed_attempt_snapshot()
    {
        var runId = await SeedRunAsync(claim: true);
        Guid attemptId;
        await using (var seed = _fixture.CreateContext())
        {
            var attempt = Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, requestedMaxTurns: 4);
            attemptId = attempt.Id;
            seed.Attempts.Add(attempt);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var result = await Handler(context).HandleAsync(
            new SetClaudeMutationTurnLimitCommand(runId, 50), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var stored = await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(4, stored.AgentRequestedMaxTurns);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(100)]
    public void Validator_accepts_null_and_the_inclusive_range(int? maxTurns)
    {
        var validation = new SetClaudeMutationTurnLimitCommandValidator()
            .Validate(new SetClaudeMutationTurnLimitCommand(Guid.NewGuid(), maxTurns));

        Assert.True(validation.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Validator_rejects_out_of_range_values_with_the_stable_code(int maxTurns)
    {
        var validation = new SetClaudeMutationTurnLimitCommandValidator()
            .Validate(new SetClaudeMutationTurnLimitCommand(Guid.NewGuid(), maxTurns));

        Assert.False(validation.IsValid);
        Assert.All(validation.Errors, error => Assert.Equal("validation.invalid", error.ErrorCode));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
