using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageWarning;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.Commands;

/// <summary>Set and clear of the run-scoped advisory Codex account-usage warning (ADR-0026) on real file-backed SQLite: one protected
/// operation with one atomic event, stable safe errors, a refusal for a terminal run and a run that does not admit Agent work, a
/// lifecycle guard that also covers a same-value request, concurrent writes that each commit their own value and event atomically
/// (the advisory column is deliberately not a concurrency token), repair of a malformed stored value, and independence from the stop.</summary>
public sealed class SetCodexAccountUsageWarningCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> SeedRunAsync(
        bool claim = true, int? percent = null, RunExecutionMode mode = RunExecutionMode.ManualAgent, int? stop = null)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        context.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, 1, mode, "Objective", Now);
        if (claim)
        {
            run.Claim(Now);
        }

        if (percent is not null)
        {
            run.SetCodexAccountUsageWarningPercent(percent);
        }

        if (stop is not null)
        {
            run.SetCodexAccountUsageStopPercent(stop);
        }

        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private static SetCodexAccountUsageWarningCommandHandler Handler(DevalCopilotDbContext context) => new(context, new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<(CodexAccountUsageWarningReading Stored, IReadOnlyList<RunEvent> Events)> StateAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        var events = await context.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.CodexAccountUsageWarningChanged)
            .OrderBy(candidate => candidate.Sequence)
            .ToListAsync();
        return (run.ReadCodexAccountUsageWarningPercent(), events);
    }

    private async Task CompleteRunAsync(Guid runId)
    {
        await using var competing = _fixture.CreateContext();
        var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.Complete(Now.AddMinutes(1));
        await competing.SaveChangesAsync();
    }

    private async Task StoreAsync(Guid runId, object? stored)
    {
        await using var writer = _fixture.CreateContext();
        await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageWarningPercent = {stored} WHERE Id = {runId}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(100)]
    public async Task A_valid_percentage_is_set_with_one_human_event(int percent)
    {
        var runId = await SeedRunAsync();
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, percent), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(percent, result.Value.Percent);
        var (stored, events) = await StateAsync(runId);
        Assert.Equal(percent, stored.Value);
        var recorded = Assert.Single(events);
        Assert.Equal("{\"percent\":" + percent + "}", recorded.PayloadJson);
        Assert.Equal(ParticipantKind.Human, recorded.ActorKind);
        Assert.Null(recorded.AttemptId);
    }

    [Fact]
    public async Task A_created_run_can_be_set_and_a_set_can_be_changed_and_cleared()
    {
        var runId = await SeedRunAsync(claim: false);

        foreach (var percent in new int?[] { 40, 70, null })
        {
            await using var context = _fixture.CreateContext();
            var result = await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, percent), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var (stored, events) = await StateAsync(runId);
        Assert.True(stored.IsAbsent);
        Assert.Equal(["{\"percent\":40}", "{\"percent\":70}", "{\"percent\":null}"], events.Select(e => e.PayloadJson));
    }

    [Fact]
    public async Task A_same_value_set_and_a_clear_of_an_unset_run_still_append_their_events()
    {
        var runId = await SeedRunAsync(percent: 50);
        var plainRunId = await SeedRunAsync();

        await using (var context = _fixture.CreateContext())
        {
            Assert.True((await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, 50), CancellationToken.None)).IsSuccess);
        }

        await using (var context = _fixture.CreateContext())
        {
            Assert.True((await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(plainRunId, null), CancellationToken.None)).IsSuccess);
        }

        Assert.Single((await StateAsync(runId)).Events);
        Assert.Single((await StateAsync(plainRunId)).Events);
    }

    [Fact]
    public async Task A_missing_run_is_not_found_with_no_event()
    {
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(Guid.NewGuid(), 50), CancellationToken.None);

        Assert.Equal(CodexAccountUsageWarningErrors.RunNotFoundCode, Assert.Single(result.Errors).Code);
        Assert.Empty(context.Events.Local);
    }

    [Fact]
    public async Task A_simulated_run_which_never_admits_agent_work_is_refused_and_keeps_no_setting()
    {
        var runId = await SeedRunAsync(mode: RunExecutionMode.Simulated);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, 50), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await StateAsync(runId);
        Assert.True(stored.IsAbsent);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(null)]
    public async Task A_terminal_run_is_not_editable_even_for_a_same_value_request_and_keeps_its_setting(int? requested)
    {
        var runId = await SeedRunAsync(percent: 50);
        await CompleteRunAsync(runId);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, requested), CancellationToken.None);

        Assert.Equal(CodexAccountUsageWarningErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await StateAsync(runId);
        Assert.Equal(50, stored.Value);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(60)]
    public async Task A_lifecycle_change_after_the_read_is_not_editable_and_rolls_the_event_back(int requested)
    {
        var runId = await SeedRunAsync(percent: 50);
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompleteRunAsync(runId)));

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, requested), CancellationToken.None);

        Assert.Equal(CodexAccountUsageWarningErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await StateAsync(runId);
        Assert.Equal(50, stored.Value);
        Assert.Empty(events);
    }

    [Fact]
    public async Task A_competing_warning_write_is_not_a_conflict_and_the_last_committed_value_and_event_agree()
    {
        var runId = await SeedRunAsync();
        await using (var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(async () =>
        {
            await using var competing = _fixture.CreateContext();
            Assert.True((await Handler(competing).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, 33), CancellationToken.None)).IsSuccess);
        })))
        {
            var result = await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, 77), CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        var (stored, events) = await StateAsync(runId);
        Assert.Equal(77, stored.Value);
        Assert.Equal(["{\"percent\":33}", "{\"percent\":77}"], events.Select(e => e.PayloadJson));
    }

    [Fact]
    public async Task Truly_concurrent_warning_writes_each_commit_atomically_and_the_final_value_matches_the_last_event()
    {
        var runId = await SeedRunAsync();
        var requests = Enumerable.Range(1, 12).Select(index => index * 5).ToArray();

        var results = await Task.WhenAll(requests.Select(async percent =>
        {
            await using var context = _fixture.CreateContext();
            return await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, percent), CancellationToken.None);
        }));

        var (stored, events) = await StateAsync(runId);
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(requests.Length, events.Count);
        Assert.Equal(requests.Order(), events.Select(item => int.Parse(item.PayloadJson[11..^1])).Order());
        Assert.Equal("{\"percent\":" + stored.Value + "}", events[^1].PayloadJson);
    }

    [Fact]
    public async Task A_concurrent_stop_change_neither_blocks_nor_is_changed_by_a_warning_write()
    {
        var runId = await SeedRunAsync(stop: 40);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, 90), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var run = await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(40, run.ReadCodexAccountUsageStopPercent().Value);
        Assert.Equal(90, run.ReadCodexAccountUsageWarningPercent().Value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(3.5)]
    [InlineData(0)]
    [InlineData(4294967297L)]
    public async Task A_valid_set_or_clear_repairs_a_malformed_stored_value(object stored)
    {
        var runId = await SeedRunAsync();
        await StoreAsync(runId, stored);

        Assert.True((await StateAsync(runId)).Stored.IsMalformed);
        await using (var context = _fixture.CreateContext())
        {
            Assert.True((await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, 25), CancellationToken.None)).IsSuccess);
        }

        Assert.Equal(25, (await StateAsync(runId)).Stored.Value);

        await StoreAsync(runId, stored);
        await using (var context = _fixture.CreateContext())
        {
            Assert.True((await Handler(context).HandleAsync(new SetCodexAccountUsageWarningCommand(runId, null), CancellationToken.None)).IsSuccess);
        }

        Assert.True((await StateAsync(runId)).Stored.IsAbsent);
    }

    [Fact]
    public async Task An_unrelated_save_preserves_a_malformed_stored_warning_without_throwing()
    {
        var runId = await SeedRunAsync();
        await StoreAsync(runId, "abc");

        await using (var context = _fixture.CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetCodexAccountUsageStopPercent(55);
            await context.SaveChangesAsync();
        }

        await using var verify = _fixture.CreateContext();
        Assert.Equal("text:abc", await verify.Database
            .SqlQuery<string>($"SELECT typeof(CodexAccountUsageWarningPercent) || ':' || CodexAccountUsageWarningPercent AS Value FROM runs WHERE Id = {runId}")
            .SingleAsync());
        var reread = await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.True(reread.ReadCodexAccountUsageWarningPercent().IsMalformed);
        Assert.Equal(55, reread.ReadCodexAccountUsageStopPercent().Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(100)]
    public void The_validator_accepts_null_and_the_inclusive_range(int? percent)
    {
        Assert.True(new SetCodexAccountUsageWarningCommandValidator().Validate(new SetCodexAccountUsageWarningCommand(Guid.NewGuid(), percent)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void The_validator_rejects_out_of_range_values_with_the_stable_code(int percent)
    {
        var result = new SetCodexAccountUsageWarningCommandValidator().Validate(new SetCodexAccountUsageWarningCommand(Guid.NewGuid(), percent));

        Assert.False(result.IsValid);
        Assert.Equal("validation.invalid", Assert.Single(result.Errors).ErrorCode);
    }
}
