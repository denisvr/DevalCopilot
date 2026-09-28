using DevalCopilot.Application.Features.Runs.Commands.SetTokenWarningThreshold;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.Commands;

public sealed class SetTokenWarningThresholdCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

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
            run.SetTokenWarningThreshold(AgentProvider.Codex, codex);
        }

        if (claude is not null)
        {
            run.SetTokenWarningThreshold(AgentProvider.ClaudeCode, claude);
        }

        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private static SetTokenWarningThresholdCommandHandler Handler(DevalCopilotDbContext context) => new(context, new FixedTimeProvider(Now));

    private async Task<(long? Codex, long? Claude, IReadOnlyList<string> Events)> ReadStateAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        var events = await context.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.TokenWarningThresholdChanged)
            .OrderBy(candidate => candidate.Sequence)
            .Select(candidate => candidate.PayloadJson)
            .ToListAsync();
        return (run.CodexTokenWarningThreshold, run.ClaudeTokenWarningThreshold, events);
    }

    private async Task CompleteRunAsync(Guid runId)
    {
        await using var competing = _fixture.CreateContext();
        var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.Complete(Now.AddMinutes(1));
        await competing.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_sets_each_provider_independently_with_one_event_each()
    {
        var runId = await SeedRunAsync();
        await using (var context = _fixture.CreateContext())
        {
            var result = await Handler(context).HandleAsync(new SetTokenWarningThresholdCommand(runId, "Codex", 1_000), CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(("Codex", 1_000L), (result.Value.Provider, result.Value.ThresholdTokens));
        }

        await using (var context = _fixture.CreateContext())
        {
            var result = await Handler(context).HandleAsync(new SetTokenWarningThresholdCommand(runId, "ClaudeCode", 50_000), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var (codex, claude, events) = await ReadStateAsync(runId);
        Assert.Equal(1_000, codex);
        Assert.Equal(50_000, claude);
        Assert.Equal(
            ["{\"provider\":\"Codex\",\"thresholdTokens\":1000}", "{\"provider\":\"ClaudeCode\",\"thresholdTokens\":50000}"],
            events);
    }

    [Fact]
    public async Task HandleAsync_clears_only_the_targeted_provider_and_records_a_null_event()
    {
        var runId = await SeedRunAsync(codex: 10, claude: 20);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenWarningThresholdCommand(runId, "Codex", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (codex, claude, events) = await ReadStateAsync(runId);
        Assert.Null(codex);
        Assert.Equal(20, claude);
        Assert.Equal("{\"provider\":\"Codex\",\"thresholdTokens\":null}", Assert.Single(events));
    }

    [Fact]
    public async Task HandleAsync_returns_not_found_for_a_missing_run()
    {
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenWarningThresholdCommand(Guid.NewGuid(), "Codex", 5), CancellationToken.None);

        Assert.Equal(TokenWarningThresholdErrors.RunNotFoundCode, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData("codex", 5L)]
    [InlineData("Claude", 5L)]
    [InlineData("", 5L)]
    [InlineData("Codex", 0L)]
    [InlineData("Codex", -1L)]
    [InlineData("ClaudeCode", Run.MaxTokenWarningThreshold + 1)]
    [InlineData("ClaudeCode", long.MaxValue)]
    public void Validator_rejects_unknown_providers_and_non_positive_or_overflowing_thresholds(string provider, long threshold)
    {
        var validation = new SetTokenWarningThresholdCommandValidator()
            .Validate(new SetTokenWarningThresholdCommand(Guid.NewGuid(), provider, threshold));

        Assert.False(validation.IsValid);
        Assert.All(validation.Errors, error => Assert.Equal("validation.invalid", error.ErrorCode));
    }

    [Theory]
    [InlineData("Codex", 1L)]
    [InlineData("ClaudeCode", Run.MaxTokenWarningThreshold)]
    public void Validator_accepts_the_inclusive_bounds_and_a_clear(string provider, long threshold)
    {
        var validator = new SetTokenWarningThresholdCommandValidator();

        Assert.True(validator.Validate(new SetTokenWarningThresholdCommand(Guid.NewGuid(), provider, threshold)).IsValid);
        Assert.True(validator.Validate(new SetTokenWarningThresholdCommand(Guid.NewGuid(), provider, null)).IsValid);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_without_a_write_when_reached_directly_with_an_invalid_value()
    {
        var runId = await SeedRunAsync(codex: 10);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenWarningThresholdCommand(runId, "Codex", 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        var (codex, _, events) = await ReadStateAsync(runId);
        Assert.Equal(10, codex);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_terminal_run_without_any_partial_write()
    {
        var runId = await SeedRunAsync(codex: 10);
        await CompleteRunAsync(runId);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetTokenWarningThresholdCommand(runId, "Codex", 99), CancellationToken.None);

        Assert.Equal(TokenWarningThresholdErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (codex, _, events) = await ReadStateAsync(runId);
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

        var result = await Handler(handlerContext).HandleAsync(new SetTokenWarningThresholdCommand(runId, "Codex", newValue), CancellationToken.None);

        Assert.Equal(TokenWarningThresholdErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (codex, _, events) = await ReadStateAsync(runId);
        Assert.Equal(10, codex);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandleAsync_keeps_both_providers_when_the_other_providers_threshold_commits_concurrently()
    {
        var runId = await SeedRunAsync();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(async () =>
        {
            await using var competing = _fixture.CreateContext();
            var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetTokenWarningThreshold(AgentProvider.ClaudeCode, 777);
            await competing.SaveChangesAsync();
        }));

        var result = await Handler(handlerContext).HandleAsync(new SetTokenWarningThresholdCommand(runId, "Codex", 5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (codex, claude, events) = await ReadStateAsync(runId);
        Assert.Equal(5, codex);
        Assert.Equal(777, claude);
        Assert.Equal("{\"provider\":\"Codex\",\"thresholdTokens\":5}", Assert.Single(events));
    }

    [Fact]
    public async Task HandleAsync_commits_the_value_and_its_event_together_when_the_same_threshold_commits_concurrently()
    {
        var runId = await SeedRunAsync();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(async () =>
        {
            await using var competing = _fixture.CreateContext();
            var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetTokenWarningThreshold(AgentProvider.Codex, 7);
            await competing.SaveChangesAsync();
        }));

        var result = await Handler(handlerContext).HandleAsync(new SetTokenWarningThresholdCommand(runId, "Codex", 9), CancellationToken.None);

        // Last committed write wins, and the one recorded event describes exactly that value.
        Assert.True(result.IsSuccess);
        var (codex, _, events) = await ReadStateAsync(runId);
        Assert.Equal(9, codex);
        Assert.Equal("{\"provider\":\"Codex\",\"thresholdTokens\":9}", Assert.Single(events));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
