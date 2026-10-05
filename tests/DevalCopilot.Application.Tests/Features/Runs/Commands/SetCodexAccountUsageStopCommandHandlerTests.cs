using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageStop;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.Commands;

/// <summary>Set and clear of the run-scoped Codex account-usage stop (ADR-0025) on real file-backed SQLite: one protected operation
/// with one atomic event, stable safe errors, a refusal for a terminal run and a run that does not admit Agent work, concurrency
/// protection against lifecycle and competing changes, repair of a malformed stored value, and no effect on an already-claimed
/// attempt's snapshot.</summary>
public sealed class SetCodexAccountUsageStopCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> SeedRunAsync(bool claim = true, int? percent = null, RunExecutionMode mode = RunExecutionMode.ManualAgent)
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
            run.SetCodexAccountUsageStopPercent(percent);
        }

        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private static SetCodexAccountUsageStopCommandHandler Handler(DevalCopilotDbContext context) => new(context, new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<(CodexAccountUsageStopReading Stored, IReadOnlyList<RunEvent> Events)> StateAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        var events = await context.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.CodexAccountUsageStopChanged)
            .OrderBy(candidate => candidate.Sequence)
            .ToListAsync();
        return (run.ReadCodexAccountUsageStopPercent(), events);
    }

    private async Task CompleteRunAsync(Guid runId)
    {
        await using var competing = _fixture.CreateContext();
        var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.Complete(Now.AddMinutes(1));
        await competing.SaveChangesAsync();
    }

    private async Task CompetingSetAsync(Guid runId, int? percent)
    {
        await using var competing = _fixture.CreateContext();
        var run = await competing.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.SetCodexAccountUsageStopPercent(percent);
        await competing.SaveChangesAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(100)]
    public async Task A_valid_percentage_is_set_with_one_human_event(int percent)
    {
        var runId = await SeedRunAsync();
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, percent), CancellationToken.None);

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
            var result = await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, percent), CancellationToken.None);
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
            Assert.True((await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, 50), CancellationToken.None)).IsSuccess);
        }

        await using (var context = _fixture.CreateContext())
        {
            Assert.True((await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(plainRunId, null), CancellationToken.None)).IsSuccess);
        }

        Assert.Single((await StateAsync(runId)).Events);
        Assert.Single((await StateAsync(plainRunId)).Events);
    }

    [Fact]
    public async Task A_missing_run_is_not_found_with_no_event()
    {
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(Guid.NewGuid(), 50), CancellationToken.None);

        Assert.Equal(CodexAccountUsageStopErrors.RunNotFoundCode, Assert.Single(result.Errors).Code);
        Assert.Empty(context.Events.Local);
    }

    [Fact]
    public async Task A_simulated_run_which_never_admits_agent_work_is_refused_and_keeps_no_setting()
    {
        var runId = await SeedRunAsync(mode: RunExecutionMode.Simulated);
        await using var context = _fixture.CreateContext();

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, 50), CancellationToken.None);

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

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, requested), CancellationToken.None);

        Assert.Equal(CodexAccountUsageStopErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await StateAsync(runId);
        Assert.Equal(50, stored.Value);
        Assert.Empty(events);
    }

    [Fact]
    public async Task A_lifecycle_change_after_the_read_is_not_editable_and_rolls_the_event_back()
    {
        var runId = await SeedRunAsync(percent: 50);
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompleteRunAsync(runId)));

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, 60), CancellationToken.None);

        Assert.Equal(CodexAccountUsageStopErrors.RunNotEditableCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await StateAsync(runId);
        Assert.Equal(50, stored.Value);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(33, 5)]
    [InlineData(null, 8)]
    public async Task A_competing_change_is_a_retryable_conflict_without_a_partial_write(int? competing, int requested)
    {
        var runId = await SeedRunAsync(percent: requested == 8 ? 8 : null);
        await using var context = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => CompetingSetAsync(runId, competing)));

        var result = await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, requested), CancellationToken.None);

        Assert.Equal(CodexAccountUsageStopErrors.ConcurrentChangeCode, Assert.Single(result.Errors).Code);
        var (stored, events) = await StateAsync(runId);
        Assert.Equal(competing, stored.Value);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(3.5)]
    [InlineData(0)]
    [InlineData(4294967297L)]
    public async Task A_valid_set_or_clear_repairs_a_malformed_stored_value(object stored)
    {
        var runId = await SeedRunAsync();
        await using (var writer = _fixture.CreateContext())
        {
            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageStopPercent = {stored} WHERE Id = {runId}");
        }

        Assert.True((await StateAsync(runId)).Stored.IsMalformed);
        await using (var context = _fixture.CreateContext())
        {
            Assert.True((await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, 25), CancellationToken.None)).IsSuccess);
        }

        Assert.Equal(25, (await StateAsync(runId)).Stored.Value);

        await using (var writer = _fixture.CreateContext())
        {
            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageStopPercent = {stored} WHERE Id = {runId}");
        }

        await using (var context = _fixture.CreateContext())
        {
            Assert.True((await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, null), CancellationToken.None)).IsSuccess);
        }

        Assert.True((await StateAsync(runId)).Stored.IsAbsent);
    }

    [Fact]
    public async Task A_change_never_touches_an_attempt_that_already_claimed_its_snapshot()
    {
        var runId = await SeedRunAsync(percent: 80);
        Guid attemptId;
        await using (var context = _fixture.CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
            var attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
            attempt.SnapshotCodexAccountUsageStop(80);
            attemptId = attempt.Id;
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            Assert.True((await Handler(context).HandleAsync(new SetCodexAccountUsageStopCommand(runId, null), CancellationToken.None)).IsSuccess);
        }

        await using var verify = _fixture.CreateContext();
        Assert.Equal(80, (await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId)).ReadAgentCodexAccountUsageStopPercent().Value);
        Assert.True((await StateAsync(runId)).Stored.IsAbsent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(100)]
    public void The_validator_accepts_null_and_the_inclusive_range(int? percent)
    {
        Assert.True(new SetCodexAccountUsageStopCommandValidator().Validate(new SetCodexAccountUsageStopCommand(Guid.NewGuid(), percent)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void The_validator_rejects_out_of_range_values_with_the_stable_code(int percent)
    {
        var result = new SetCodexAccountUsageStopCommandValidator().Validate(new SetCodexAccountUsageStopCommand(Guid.NewGuid(), percent));

        Assert.False(result.IsValid);
        Assert.Equal("validation.invalid", Assert.Single(result.Errors).ErrorCode);
    }
}
