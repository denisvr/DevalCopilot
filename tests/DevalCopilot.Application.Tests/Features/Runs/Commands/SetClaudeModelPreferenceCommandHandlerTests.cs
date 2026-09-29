using DevalCopilot.Application.Features.Runs.Commands.SetClaudeModelPreference;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.Commands;

public sealed class SetClaudeModelPreferenceCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> SeedRunAsync(bool claim = false, string? alias = null)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        context.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        if (claim)
        {
            run.Claim(Now);
        }

        if (alias is not null)
        {
            run.SetRequestedClaudeModelRequest(alias, null);
        }

        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private static SetClaudeModelPreferenceCommandHandler Handler(DevalCopilotDbContext context) =>
        new(context, new FixedTimeProvider(Now));

    private async Task<(string? Model, IReadOnlyList<string> Events)> ReadStateAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        var events = await context.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.ClaudeModelPreferenceChanged)
            .OrderBy(candidate => candidate.Sequence)
            .Select(candidate => candidate.PayloadJson)
            .ToListAsync();
        return (run.RequestedClaudeModel, events);
    }

    private async Task CompleteRunAsync(Guid runId)
    {
        await using var competing = _fixture.CreateContext();
        var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.Complete(Now.AddMinutes(1));
        await competing.SaveChangesAsync();
    }

    [Theory]
    [InlineData("sonnet")]
    [InlineData("opus")]
    [InlineData("haiku")]
    public async Task HandleAsync_sets_each_supported_alias_and_records_one_event(string alias)
    {
        var runId = await SeedRunAsync(claim: true);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetClaudeModelPreferenceCommand(runId, alias), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(alias, result.Value.RequestedModel);
        var (model, events) = await ReadStateAsync(runId);
        Assert.Equal(alias, model);
        Assert.Equal("{\"requestedModel\":\"" + alias + "\",\"requestedEffort\":null}", Assert.Single(events));
    }

    [Fact]
    public async Task HandleAsync_clears_the_preference_and_records_a_null_event()
    {
        var runId = await SeedRunAsync(alias: "opus");
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetClaudeModelPreferenceCommand(runId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.RequestedModel);
        var (model, events) = await ReadStateAsync(runId);
        Assert.Null(model);
        Assert.Equal("{\"requestedModel\":null,\"requestedEffort\":null}", Assert.Single(events));
    }

    [Fact]
    public async Task HandleAsync_returns_not_found_for_a_missing_run()
    {
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetClaudeModelPreferenceCommand(Guid.NewGuid(), "opus"), CancellationToken.None);

        Assert.Equal(ClaudeModelPreferenceErrors.RunNotFoundCode, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData("Opus")]
    [InlineData(" opus")]
    [InlineData("fable")]
    [InlineData("claude-opus-5-5")]
    [InlineData("")]
    public async Task Validator_and_domain_reject_anything_outside_the_closed_alias_set(string alias)
    {
        var validation = new SetClaudeModelPreferenceCommandValidator()
            .Validate(new SetClaudeModelPreferenceCommand(Guid.NewGuid(), alias));
        Assert.False(validation.IsValid);
        Assert.Equal("validation.invalid", validation.Errors[0].ErrorCode);

        var runId = await SeedRunAsync(alias: "sonnet");
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        Assert.Throws<ArgumentException>(() => run.SetRequestedClaudeModelRequest(alias, null));
        Assert.Equal("sonnet", run.RequestedClaudeModel);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_terminal_run_without_any_partial_write()
    {
        var runId = await SeedRunAsync(claim: true, alias: "sonnet");
        await CompleteRunAsync(runId);

        await using var handlerContext = _fixture.CreateContext();
        var result = await Handler(handlerContext).HandleAsync(new SetClaudeModelPreferenceCommand(runId, "opus"), CancellationToken.None);

        Assert.Equal(ClaudeModelPreferenceErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (model, events) = await ReadStateAsync(runId);
        Assert.Equal("sonnet", model);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandleAsync_reports_a_lifecycle_transition_committed_after_its_read_as_not_editable_with_no_event()
    {
        var runId = await SeedRunAsync(claim: true, alias: "sonnet");
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompleteRunAsync(runId)));

        var result = await Handler(handlerContext).HandleAsync(new SetClaudeModelPreferenceCommand(runId, "opus"), CancellationToken.None);

        Assert.Equal(ClaudeModelPreferenceErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (model, events) = await ReadStateAsync(runId);
        Assert.Equal("sonnet", model);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandleAsync_reports_a_lifecycle_transition_after_a_no_op_set_and_still_appends_no_event()
    {
        var runId = await SeedRunAsync(claim: true, alias: "sonnet");
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompleteRunAsync(runId)));

        // Same alias as already stored: without the forced UPDATE this would append an event to a
        // Run that had become terminal.
        var result = await Handler(handlerContext).HandleAsync(new SetClaudeModelPreferenceCommand(runId, "sonnet"), CancellationToken.None);

        Assert.Equal(ClaudeModelPreferenceErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        Assert.Empty((await ReadStateAsync(runId)).Events);
    }

    [Fact]
    public async Task HandleAsync_reports_a_competing_preference_change_as_a_retryable_conflict_without_partial_write()
    {
        var runId = await SeedRunAsync(claim: true);
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => ClaudeModelPreferenceTestSupport.SetPreferenceAsync(_fixture, runId, "haiku")));

        var result = await Handler(handlerContext).HandleAsync(new SetClaudeModelPreferenceCommand(runId, "opus"), CancellationToken.None);

        Assert.Equal(ClaudeModelPreferenceErrors.ConcurrentChangeCode, Assert.Single(result.Errors).Code);
        var (model, events) = await ReadStateAsync(runId);
        Assert.Equal("haiku", model);
        Assert.Empty(events);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<(string? Model, string? Effort)> ReadPairAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        return (run.RequestedClaudeModel, run.RequestedClaudeEffort);
    }

    [Theory]
    [InlineData("sonnet", "low")]
    [InlineData("sonnet", "medium")]
    [InlineData("opus", "high")]
    public async Task HandleAsync_persists_the_pair_and_records_one_event_carrying_both_values(string model, string effort)
    {
        var runId = await SeedRunAsync(claim: true);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(
            new SetClaudeModelPreferenceCommand(runId, model, effort), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(model, result.Value.RequestedModel);
        Assert.Equal(effort, result.Value.RequestedEffort);
        Assert.Equal((model, effort), await ReadPairAsync(runId));
        var (_, events) = await ReadStateAsync(runId);
        Assert.Equal("{\"requestedModel\":\"" + model + "\",\"requestedEffort\":\"" + effort + "\"}", Assert.Single(events));
    }

    [Fact]
    public async Task HandleAsync_clearing_removes_the_effort_with_the_model_in_one_event()
    {
        var runId = await SeedRunAsync(claim: true);
        await ClaudeModelPreferenceTestSupport.SetPreferenceAsync(_fixture, runId, "opus", "high");
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetClaudeModelPreferenceCommand(runId, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((null, null), await ReadPairAsync(runId));
        var (_, events) = await ReadStateAsync(runId);
        Assert.Equal("{\"requestedModel\":null,\"requestedEffort\":null}", Assert.Single(events));
    }

    [Theory]
    [InlineData(null, "low")]
    [InlineData("haiku", "low")]
    [InlineData("sonnet", "Low")]
    [InlineData("sonnet", "HIGH")]
    [InlineData("sonnet", " high")]
    [InlineData("opus", "")]
    [InlineData("opus", "max")]
    [InlineData("opus", "xhigh")]
    public void Validator_rejects_an_invalid_pair(string? model, string? effort)
    {
        var validation = new SetClaudeModelPreferenceCommandValidator()
            .Validate(new SetClaudeModelPreferenceCommand(Guid.NewGuid(), model, effort));

        Assert.False(validation.IsValid);
        Assert.All(validation.Errors, error => Assert.Equal("validation.invalid", error.ErrorCode));
    }

    [Theory]
    [InlineData("sonnet", "low")]
    [InlineData("opus", "high")]
    [InlineData("opus", null)]
    [InlineData("haiku", null)]
    [InlineData(null, null)]
    public void Validator_accepts_each_valid_pair(string? model, string? effort)
    {
        var validation = new SetClaudeModelPreferenceCommandValidator()
            .Validate(new SetClaudeModelPreferenceCommand(Guid.NewGuid(), model, effort));

        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_terminal_run_for_a_pair_without_any_partial_write()
    {
        var runId = await SeedRunAsync(claim: true);
        await ClaudeModelPreferenceTestSupport.SetPreferenceAsync(_fixture, runId, "sonnet", "low");
        await CompleteRunAsync(runId);

        await using var context = _fixture.CreateContext();
        var result = await Handler(context).HandleAsync(new SetClaudeModelPreferenceCommand(runId, "opus", "high"), CancellationToken.None);

        Assert.Equal(ClaudeModelPreferenceErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        Assert.Equal(("sonnet", "low"), await ReadPairAsync(runId));
        Assert.Empty((await ReadStateAsync(runId)).Events);
    }

    [Fact]
    public async Task HandleAsync_reports_a_competing_effort_only_change_as_a_conflict_with_no_partial_write()
    {
        var runId = await SeedRunAsync(claim: true);
        await ClaudeModelPreferenceTestSupport.SetPreferenceAsync(_fixture, runId, "opus", "low");
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => ClaudeModelPreferenceTestSupport.SetPreferenceAsync(_fixture, runId, "opus", "high")));

        // The handler read ("opus", "low") and asks for ("opus", "medium"). The competitor changes only
        // the effort (same model), so only the effort concurrency token can detect it.
        var result = await Handler(handlerContext).HandleAsync(
            new SetClaudeModelPreferenceCommand(runId, "opus", "medium"), CancellationToken.None);

        Assert.Equal(ClaudeModelPreferenceErrors.ConcurrentChangeCode, Assert.Single(result.Errors).Code);
        Assert.Equal(("opus", "high"), await ReadPairAsync(runId));
        Assert.Empty((await ReadStateAsync(runId)).Events);
    }
}
