using DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.Commands;

public sealed class SetTokenStopThresholdCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> SeedRunAsync(bool claim = true, long? codex = null, long? claude = null)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        context.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        if (claim)
        {
            run.Claim(Now);
        }

        if (codex is not null)
        {
            run.SetTokenStopThreshold(AgentProvider.Codex, codex);
        }

        if (claude is not null)
        {
            run.SetTokenStopThreshold(AgentProvider.ClaudeCode, claude);
        }

        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private static SetTokenStopThresholdCommandHandler Handler(DevalCopilotDbContext context) => new(context, new FixedTimeProvider(Now));

    private async Task<(long? Codex, long? Claude, long? CodexWarning, IReadOnlyList<string> Events)> ReadStateAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        var events = await context.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.TokenStopThresholdChanged)
            .OrderBy(candidate => candidate.Sequence)
            .Select(candidate => candidate.PayloadJson)
            .ToListAsync();
        return (run.CodexTokenStopThreshold, run.ClaudeTokenStopThreshold, run.CodexTokenWarningThreshold, events);
    }

    private async Task CompleteRunAsync(Guid runId)
    {
        await using var competing = _fixture.CreateContext();
        var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.Complete(Now.AddMinutes(1));
        await competing.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_sets_each_provider_independently_with_one_event_each_and_no_warning_write()
    {
        var runId = await SeedRunAsync();
        await using (var context = _fixture.CreateContext())
        {
            var result = await Handler(context).HandleAsync(new SetTokenStopThresholdCommand(runId, "Codex", 1_000), CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(("Codex", 1_000L), (result.Value.Provider, result.Value.ThresholdTokens));
        }

        await using (var context = _fixture.CreateContext())
        {
            var result = await Handler(context).HandleAsync(new SetTokenStopThresholdCommand(runId, "ClaudeCode", 50_000), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var (codex, claude, codexWarning, events) = await ReadStateAsync(runId);
        Assert.Equal(1_000, codex);
        Assert.Equal(50_000, claude);
        Assert.Null(codexWarning);
        Assert.Equal(
            ["{\"provider\":\"Codex\",\"thresholdTokens\":1000}", "{\"provider\":\"ClaudeCode\",\"thresholdTokens\":50000}"],
            events);
    }

    [Fact]
    public async Task HandleAsync_can_configure_a_created_run_before_it_is_claimed()
    {
        var runId = await SeedRunAsync(claim: false);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenStopThresholdCommand(runId, "Codex", 5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, (await ReadStateAsync(runId)).Codex);
    }

    [Fact]
    public async Task HandleAsync_clears_only_the_targeted_provider_and_records_a_null_event()
    {
        var runId = await SeedRunAsync(codex: 10, claude: 20);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenStopThresholdCommand(runId, "Codex", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (codex, claude, _, events) = await ReadStateAsync(runId);
        Assert.Null(codex);
        Assert.Equal(20, claude);
        Assert.Equal("{\"provider\":\"Codex\",\"thresholdTokens\":null}", Assert.Single(events));
    }

    [Fact]
    public async Task HandleAsync_returns_not_found_for_a_missing_run()
    {
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenStopThresholdCommand(Guid.NewGuid(), "Codex", 5), CancellationToken.None);

        Assert.Equal(TokenStopThresholdErrors.RunNotFoundCode, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData("codex", 5L)]
    [InlineData("Claude", 5L)]
    [InlineData("", 5L)]
    [InlineData("Codex", 0L)]
    [InlineData("Codex", -1L)]
    [InlineData("ClaudeCode", Run.MaxTokenStopThreshold + 1)]
    [InlineData("ClaudeCode", long.MaxValue)]
    public void Validator_rejects_unknown_providers_and_non_positive_or_overflowing_thresholds(string provider, long threshold)
    {
        var validation = new SetTokenStopThresholdCommandValidator()
            .Validate(new SetTokenStopThresholdCommand(Guid.NewGuid(), provider, threshold));

        Assert.False(validation.IsValid);
        Assert.All(validation.Errors, error => Assert.Equal("validation.invalid", error.ErrorCode));
    }

    [Theory]
    [InlineData("Codex", 1L)]
    [InlineData("ClaudeCode", Run.MaxTokenStopThreshold)]
    public void Validator_accepts_the_inclusive_bounds_and_a_clear(string provider, long threshold)
    {
        var validator = new SetTokenStopThresholdCommandValidator();

        Assert.True(validator.Validate(new SetTokenStopThresholdCommand(Guid.NewGuid(), provider, threshold)).IsValid);
        Assert.True(validator.Validate(new SetTokenStopThresholdCommand(Guid.NewGuid(), provider, null)).IsValid);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_without_a_write_when_reached_directly_with_an_invalid_value()
    {
        var runId = await SeedRunAsync(codex: 10);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenStopThresholdCommand(runId, "Codex", 0), CancellationToken.None);

        Assert.Equal(TokenStopThresholdErrors.InvalidCode, Assert.Single(result.Errors).Code);
        var (codex, _, _, events) = await ReadStateAsync(runId);
        Assert.Equal(10, codex);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_for_an_unknown_provider_reached_directly()
    {
        var runId = await SeedRunAsync(codex: 10);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenStopThresholdCommand(runId, "Gemini", 5), CancellationToken.None);

        Assert.Equal(TokenStopThresholdErrors.InvalidCode, Assert.Single(result.Errors).Code);
        Assert.Empty((await ReadStateAsync(runId)).Events);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_terminal_run_without_any_partial_write()
    {
        var runId = await SeedRunAsync(codex: 10);
        await CompleteRunAsync(runId);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenStopThresholdCommand(runId, "Codex", 99), CancellationToken.None);

        Assert.Equal(TokenStopThresholdErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (codex, _, _, events) = await ReadStateAsync(runId);
        Assert.Equal(10, codex);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(99L)]
    [InlineData(10L)]
    public async Task HandleAsync_reports_a_lifecycle_transition_committed_after_its_read_as_not_editable_with_no_event(long newValue)
    {
        // 10 is the already-stored value: a no-op set must still be guarded against the transition.
        var runId = await SeedRunAsync(codex: 10);
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompleteRunAsync(runId)));

        var result = await Handler(handlerContext).HandleAsync(new SetTokenStopThresholdCommand(runId, "Codex", newValue), CancellationToken.None);

        Assert.Equal(TokenStopThresholdErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (codex, _, _, events) = await ReadStateAsync(runId);
        Assert.Equal(10, codex);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(AgentProvider.Codex)]
    [InlineData(AgentProvider.ClaudeCode)]
    public async Task HandleAsync_reports_a_stop_threshold_committed_concurrently_as_a_retryable_conflict_with_no_event(
        AgentProvider competingProvider)
    {
        var runId = await SeedRunAsync();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => TokenStopTestSupport.SetStopAsync(_fixture, runId, competingProvider, 777)));

        var result = await Handler(handlerContext).HandleAsync(new SetTokenStopThresholdCommand(runId, "Codex", 5), CancellationToken.None);

        // The stop columns are concurrency tokens, so neither writer silently overwrites the other:
        // the loser rolls back its value and its event together and is told to retry.
        Assert.Equal(TokenStopThresholdErrors.ConcurrentChangeCode, Assert.Single(result.Errors).Code);
        var (codex, claude, _, events) = await ReadStateAsync(runId);
        Assert.Equal(competingProvider == AgentProvider.Codex ? 777 : (long?)null, codex);
        Assert.Equal(competingProvider == AgentProvider.ClaudeCode ? 777 : (long?)null, claude);
        Assert.Empty(events);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
