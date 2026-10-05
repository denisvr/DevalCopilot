using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageWarning;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.AccountUsageStopTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The explicit advisory warning check (ADR-0026) on real file-backed SQLite: no setting, an invalid setting, an unadmitted
/// run and a missing launch make no observation; otherwise exactly one observation is made outside any transaction for the exact
/// vetted launch, the stored setting, mode and launch are re-read afterwards so a replaced authority is never an applicable result,
/// a populated change tracker never supplies authority, the stop setting is irrelevant, and the check writes nothing.</summary>
public sealed class GetCodexAccountUsageWarningQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private const string Executable = @"C:\safe\codex.exe";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Saves { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private async Task<Guid> SeedRunAsync(
        int? warning = null, int? stop = null, RunExecutionMode mode = RunExecutionMode.ManualAgent, bool launch = true)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        context.Projects.Add(project);
        var run = mode == RunExecutionMode.Legacy
            ? Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now)
            : Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, 1, mode, "Objective", Now);
        run.Claim(Now);
        run.SetCodexAccountUsageWarningPercent(warning);
        run.SetCodexAccountUsageStopPercent(stop);
        context.Runs.Add(run);
        if (launch && !await context.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.CodexCli))
        {
            var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
            snapshot.MarkDispatched(Now);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, Executable, null, "1.2.3", Now, Now.AddMinutes(5));
            context.HostCapabilitySnapshots.Add(snapshot);
        }

        await context.SaveChangesAsync();
        return run.Id;
    }

    private static GetCodexAccountUsageWarningQueryHandler Handler(
        DevalCopilotDbContext context, IAccountUsageObserver observer, TimeProvider? clock = null) =>
        new(context, observer, clock ?? new MutableClock(Now));

    private async Task<GetCodexAccountUsageWarningQueryResult> CheckAsync(
        Guid runId, IAccountUsageObserver observer, TimeProvider? clock = null)
    {
        await using var context = _fixture.CreateContext();
        var result = await Handler(context, observer, clock).HandleAsync(new GetCodexAccountUsageWarningQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static async Task SetWarningAsync(SqliteDatabaseFixture fixture, Guid runId, int? percent)
    {
        await using var context = fixture.CreateContext();
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.SetCodexAccountUsageWarningPercent(percent);
        context.Entry(run).Property(Run.CodexAccountUsageWarningStorageProperty).IsModified = true;
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_missing_run_is_not_found_with_no_observation()
    {
        var observer = StubAccountUsageAdapter.Always(Observation(Now, 1));
        await using var context = _fixture.CreateContext();

        var result = await Handler(context, observer).HandleAsync(new GetCodexAccountUsageWarningQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(CodexAccountUsageWarningErrors.RunNotFoundCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, observer.Calls);
    }

    [Fact]
    public async Task An_unadmitted_run_is_refused_with_no_observation()
    {
        var runId = await SeedRunAsync(warning: 50, mode: RunExecutionMode.Simulated);
        var observer = StubAccountUsageAdapter.Always(Observation(Now, 1));
        await using var context = _fixture.CreateContext();

        var result = await Handler(context, observer).HandleAsync(new GetCodexAccountUsageWarningQuery(runId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, observer.Calls);
    }

    [Fact]
    public async Task No_setting_is_not_configured_with_no_observation_even_when_a_stop_is_saved()
    {
        var runId = await SeedRunAsync(warning: null, stop: 10);
        var observer = StubAccountUsageAdapter.Always(Observation(Now, 99));

        var result = await CheckAsync(runId, observer);

        Assert.Equal(CodexAccountUsageWarningCheckState.NotConfigured, result.State);
        Assert.Null(result.ThresholdPercent);
        Assert.Equal(0, observer.Calls);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(3.5)]
    [InlineData(0)]
    [InlineData(101)]
    public async Task An_invalid_stored_setting_is_reported_invalid_with_no_observation(object stored)
    {
        var runId = await SeedRunAsync(warning: 50);
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageWarningPercent = {stored} WHERE Id = {runId}");
        }

        var observer = StubAccountUsageAdapter.Always(Observation(Now, 1));
        var result = await CheckAsync(runId, observer);

        Assert.Equal(CodexAccountUsageWarningCheckState.SettingInvalid, result.State);
        Assert.Null(result.ThresholdPercent);
        Assert.Empty(result.Windows);
        Assert.Equal(0, observer.Calls);
    }

    [Fact]
    public async Task A_missing_launch_target_is_unavailable_with_no_observation()
    {
        var runId = await SeedRunAsync(warning: 50, launch: false);
        var observer = StubAccountUsageAdapter.Always(Observation(Now, 1));

        var result = await CheckAsync(runId, observer);

        Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, result.State);
        Assert.Equal(CodexAccountUsageWarningReason.EvidenceUnavailable, result.Reason);
        Assert.Equal(0, observer.Calls);
    }

    [Theory]
    [InlineData(79, CodexAccountUsageWarningCheckState.Below)]
    [InlineData(80, CodexAccountUsageWarningCheckState.Reached)]
    [InlineData(95, CodexAccountUsageWarningCheckState.Reached)]
    public async Task A_configured_check_makes_exactly_one_observation_for_the_exact_vetted_launch(
        int used, CodexAccountUsageWarningCheckState expected)
    {
        var runId = await SeedRunAsync(warning: 80);
        var observer = StubAccountUsageAdapter.Always(Observation(Now, used, 5));

        var result = await CheckAsync(runId, observer);

        Assert.Equal(expected, result.State);
        Assert.Equal(80, result.ThresholdPercent);
        Assert.Equal(Now, result.ObservedAtUtc);
        Assert.Equal(2, result.Windows.Count);
        Assert.Equal(1, observer.Calls);
        Assert.Equal((Executable, (string?)null), Assert.Single(observer.Tuples));
    }

    [Fact]
    public async Task The_warning_and_stop_thresholds_are_independent_with_no_required_ordering()
    {
        var warningBelowStop = await SeedRunAsync(warning: 30, stop: 90);
        var warningAboveStop = await SeedRunAsync(warning: 90, stop: 30);

        Assert.Equal(
            CodexAccountUsageWarningCheckState.Reached, (await CheckAsync(warningBelowStop, StubAccountUsageAdapter.Always(Observation(Now, 50)))).State);
        Assert.Equal(
            CodexAccountUsageWarningCheckState.Below, (await CheckAsync(warningAboveStop, StubAccountUsageAdapter.Always(Observation(Now, 50)))).State);
    }

    [Fact]
    public async Task A_provider_reported_reached_state_warns_and_an_unavailable_observation_never_reads_as_below()
    {
        var runId = await SeedRunAsync(warning: 80);

        var reached = await CheckAsync(runId, StubAccountUsageAdapter.Always(Observation(Now, 1, providerReached: true)));
        var unavailable = await CheckAsync(runId, StubAccountUsageAdapter.Always(AccountUsageObservation.Unavailable));
        var throwing = await CheckAsync(runId, new StubAccountUsageAdapter((_, _, _, _) => throw new InvalidOperationException("boom")));

        Assert.Equal(CodexAccountUsageWarningReason.ProviderReportedLimitReached, reached.Reason);
        Assert.Equal(CodexAccountUsageWarningCheckState.Reached, reached.State);
        foreach (var result in new[] { unavailable, throwing })
        {
            Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, result.State);
            Assert.Equal(CodexAccountUsageWarningReason.EvidenceUnavailable, result.Reason);
            Assert.Null(result.ObservedAtUtc);
            Assert.Empty(result.Windows);
        }
    }

    [Fact]
    public async Task The_callers_cancellation_propagates_and_is_not_an_unavailable_result()
    {
        var runId = await SeedRunAsync(warning: 80);
        using var cancellation = new CancellationTokenSource();
        var observer = new StubAccountUsageAdapter(async (_, _, _, token) =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return AccountUsageObservation.Unavailable;
        });
        await using var context = _fixture.CreateContext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Handler(context, observer).HandleAsync(new GetCodexAccountUsageWarningQuery(runId), cancellation.Token));
    }

    [Fact]
    public async Task Evidence_that_is_no_longer_current_when_it_is_evaluated_is_expired_not_below()
    {
        var runId = await SeedRunAsync(warning: 80);
        var clock = new MutableClock(Now);
        var observer = new StubAccountUsageAdapter(_ => Observation(Now, 1));

        var current = await CheckAsync(runId, observer, clock);
        var stale = await CheckAsync(
            runId,
            new StubAccountUsageAdapter(_ =>
            {
                var observation = Observation(clock.Now, 1);
                clock.Now = clock.Now.AddSeconds(31);
                return observation;
            }),
            clock);

        Assert.Equal(CodexAccountUsageWarningCheckState.Below, current.State);
        Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, stale.State);
        Assert.Equal(CodexAccountUsageWarningReason.EvidenceExpired, stale.Reason);
    }

    [Fact]
    public async Task A_retrieval_instant_outside_the_host_read_interval_is_expired_evidence()
    {
        var runId = await SeedRunAsync(warning: 80);

        var result = await CheckAsync(runId, StubAccountUsageAdapter.Always(Observation(Now.AddSeconds(-5), 1)));

        Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, result.State);
        Assert.Equal(CodexAccountUsageWarningReason.EvidenceExpired, result.Reason);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(null)]
    public async Task A_setting_replaced_during_the_read_is_never_an_applicable_result(int? replacement)
    {
        var runId = await SeedRunAsync(warning: 80);
        var observer = new StubAccountUsageAdapter(async (_, _, _, _) =>
        {
            await SetWarningAsync(_fixture, runId, replacement);
            return Observation(Now, 1);
        });

        var result = await CheckAsync(runId, observer);

        Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, result.State);
        Assert.Equal(CodexAccountUsageWarningReason.ConfigurationChanged, result.Reason);
        Assert.Empty(result.Windows);
        Assert.Null(result.ObservedAtUtc);
        Assert.Equal(1, observer.Calls);
    }

    [Fact]
    public async Task A_launch_replaced_during_the_read_is_never_an_applicable_result()
    {
        var runId = await SeedRunAsync(warning: 80);
        var observer = new StubAccountUsageAdapter(async (_, _, _, _) =>
        {
            await ChangeLaunchTargetAsync(_fixture, @"C:\other\codex.exe");
            return Observation(Now, 1);
        });

        var result = await CheckAsync(runId, observer);

        Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, result.State);
        Assert.Equal(CodexAccountUsageWarningReason.ConfigurationChanged, result.Reason);
    }

    [Fact]
    public async Task A_launch_removed_during_the_read_is_never_an_applicable_result()
    {
        var runId = await SeedRunAsync(warning: 80);
        var observer = new StubAccountUsageAdapter(async (_, _, _, _) =>
        {
            await using var context = _fixture.CreateContext();
            await context.HostCapabilitySnapshots.ExecuteDeleteAsync();
            return Observation(Now, 1);
        });

        Assert.Equal(CodexAccountUsageWarningReason.ConfigurationChanged, (await CheckAsync(runId, observer)).Reason);
    }

    [Theory]
    [InlineData(RunExecutionMode.ManualAgent, RunExecutionMode.Legacy)]
    [InlineData(RunExecutionMode.Legacy, RunExecutionMode.ManualAgent)]
    public async Task A_different_admitted_mode_committed_during_the_read_is_never_an_applicable_result(
        RunExecutionMode original, RunExecutionMode replacement)
    {
        var runId = await SeedRunAsync(warning: 80, mode: original);
        var observer = new StubAccountUsageAdapter(async (_, _, _, _) =>
        {
            await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)replacement);
            return Observation(Now, 1);
        });

        var before = await CountsAsync();
        var result = await CheckAsync(runId, observer);

        AssertConfigurationChanged(result);
        Assert.Equal(1, observer.Calls);
        Assert.Equal(before, await CountsAsync());
    }

    [Theory]
    [InlineData(RunExecutionMode.ManualAgent, "1")]
    [InlineData(RunExecutionMode.Legacy, "1")]
    [InlineData(RunExecutionMode.ManualAgent, "7")]
    [InlineData(RunExecutionMode.ManualAgent, "-1")]
    [InlineData(RunExecutionMode.ManualAgent, "2.5")]
    [InlineData(RunExecutionMode.Legacy, "'two'")]
    [InlineData(RunExecutionMode.ManualAgent, "X'02'")]
    [InlineData(RunExecutionMode.ManualAgent, "4294967298")]
    public async Task A_mode_replaced_by_an_unadmitted_or_malformed_one_during_the_read_is_never_an_applicable_result(
        RunExecutionMode original, string literal)
    {
        var runId = await SeedRunAsync(warning: 80, mode: original);
        var observer = new StubAccountUsageAdapter(async (_, _, _, _) =>
        {
            await RunExecutionModeTestSupport.SetStoredRawAsync(_fixture, runId, literal);
            return Observation(Now, 1);
        });

        var before = await CountsAsync();
        var result = await CheckAsync(runId, observer);

        AssertConfigurationChanged(result);
        Assert.Equal(1, observer.Calls);
        Assert.Equal(before, await CountsAsync());
    }

    [Theory]
    [InlineData(RunExecutionMode.ManualAgent)]
    [InlineData(RunExecutionMode.Legacy)]
    public async Task An_unchanged_mode_keeps_the_applicable_result_even_when_it_is_rewritten_to_the_same_value(RunExecutionMode mode)
    {
        var runId = await SeedRunAsync(warning: 80, mode: mode);
        var plain = new StubAccountUsageAdapter(_ => Observation(Now, 10));
        var rewritten = new StubAccountUsageAdapter(async (_, _, _, _) =>
        {
            await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)mode);
            return Observation(Now, 90);
        });

        var below = await CheckAsync(runId, plain);
        var reached = await CheckAsync(runId, rewritten);

        Assert.Equal(CodexAccountUsageWarningCheckState.Below, below.State);
        Assert.Equal(CodexAccountUsageWarningCheckState.Reached, reached.State);
        Assert.Equal(1, plain.Calls);
        Assert.Equal(1, rewritten.Calls);
    }

    [Theory]
    [InlineData(RunExecutionMode.ManualAgent, RunExecutionMode.Legacy)]
    [InlineData(RunExecutionMode.Legacy, RunExecutionMode.ManualAgent)]
    public async Task A_populated_tracker_never_supplies_the_mode_before_or_after_the_read(RunExecutionMode original, RunExecutionMode replacement)
    {
        var runId = await SeedRunAsync(warning: 80, mode: original);
        await using var context = _fixture.CreateContext();
        var tracked = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(original, tracked.ExecutionMode);
        var observer = new StubAccountUsageAdapter(async (_, _, _, _) =>
        {
            await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)replacement);
            return Observation(Now, 1);
        });

        var result = await Handler(context, observer).HandleAsync(new GetCodexAccountUsageWarningQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertConfigurationChanged(result.Value);
        Assert.Equal(original, tracked.ExecutionMode);
        Assert.Equal(1, observer.Calls);

        // A later check starts from the freshly stored replacement, not from the stale tracked mode.
        var fresh = StubAccountUsageAdapter.Always(Observation(Now, 1));
        var next = await Handler(context, fresh).HandleAsync(new GetCodexAccountUsageWarningQuery(runId), CancellationToken.None);
        Assert.Equal(CodexAccountUsageWarningCheckState.Below, next.Value.State);
        Assert.Equal(1, fresh.Calls);
    }

    [Fact]
    public async Task A_mode_that_is_unadmitted_when_the_check_starts_makes_no_observation_even_with_a_tracked_admitted_run()
    {
        var runId = await SeedRunAsync(warning: 80);
        await using var context = _fixture.CreateContext();
        await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)RunExecutionMode.Simulated);
        var observer = StubAccountUsageAdapter.Always(Observation(Now, 1));

        var result = await Handler(context, observer).HandleAsync(new GetCodexAccountUsageWarningQuery(runId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, observer.Calls);
    }

    private static void AssertConfigurationChanged(GetCodexAccountUsageWarningQueryResult result)
    {
        Assert.Equal(CodexAccountUsageWarningCheckState.Unavailable, result.State);
        Assert.Equal(CodexAccountUsageWarningReason.ConfigurationChanged, result.Reason);
        Assert.Empty(result.Windows);
        Assert.Null(result.ObservedAtUtc);
        Assert.False(result.ProviderReportedLimitReached);
    }

    private async Task<(int Events, int Attempts)> CountsAsync()
    {
        await using var context = _fixture.CreateContext();
        return (await context.Events.CountAsync(), await context.Attempts.CountAsync());
    }

    [Fact]
    public async Task A_populated_change_tracker_never_supplies_the_setting_or_the_launch()
    {
        var runId = await SeedRunAsync(warning: 10);
        await using var context = _fixture.CreateContext();
        var tracked = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        var trackedSnapshot = await context.HostCapabilitySnapshots.SingleAsync(candidate => candidate.Capability == Capability.CodexCli);
        Assert.Equal(10, tracked.ReadCodexAccountUsageWarningPercent().Value);
        await SetWarningAsync(_fixture, runId, 60);
        await ChangeLaunchTargetAsync(_fixture, @"C:\fresh\codex.exe");
        var observer = StubAccountUsageAdapter.Always(Observation(Now, 50));

        var result = await Handler(context, observer).HandleAsync(new GetCodexAccountUsageWarningQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CodexAccountUsageWarningCheckState.Below, result.Value.State);
        Assert.Equal(60, result.Value.ThresholdPercent);
        Assert.Equal((@"C:\fresh\codex.exe", (string?)null), Assert.Single(observer.Tuples));
        Assert.Equal(10, tracked.ReadCodexAccountUsageWarningPercent().Value);
        Assert.Equal(@"C:\safe\codex.exe", trackedSnapshot.ResolvedExecutablePath);
    }

    [Fact]
    public async Task A_check_writes_nothing_and_creates_no_attempt_event_or_history()
    {
        var runId = await SeedRunAsync(warning: 50, stop: 20);
        async Task<(int Events, int Attempts, string Stored)> SnapshotAsync()
        {
            await using var context = _fixture.CreateContext();
            return (
                await context.Events.CountAsync(),
                await context.Attempts.CountAsync(),
                await context.Database.SqlQuery<string>(
                    $"SELECT CodexAccountUsageWarningPercent || '|' || CodexAccountUsageStopPercent AS Value FROM runs WHERE Id = {runId}")
                    .SingleAsync());
        }

        var before = await SnapshotAsync();
        var counter = new SaveCounter();
        await using (var context = _fixture.CreateContext(counter))
        {
            foreach (var used in new[] { 10, 90 })
            {
                var result = await Handler(context, StubAccountUsageAdapter.Always(Observation(Now, used)))
                    .HandleAsync(new GetCodexAccountUsageWarningQuery(runId), CancellationToken.None);
                Assert.True(result.IsSuccess);
            }

            Assert.Empty(context.ChangeTracker.Entries());
        }

        Assert.Equal(0, counter.Saves);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task The_result_discloses_only_bounded_percentages_identifiers_and_the_host_retrieval_time()
    {
        var runId = await SeedRunAsync(warning: 50);
        var observer = StubAccountUsageAdapter.Always(Observation(Now, 60, 20, bucket: "codex_other"));

        var result = await CheckAsync(runId, observer);

        Assert.Equal(
            [("codex_other", CodexAccountUsageWarningWindowKind.Primary, 60, true), ("codex_other", CodexAccountUsageWarningWindowKind.Secondary, 20, false)],
            result.Windows.Select(window => (window.BucketId, window.Kind, window.UsedPercent, window.ReachedThreshold)));
        Assert.False(result.ProviderReportedLimitReached);
    }
}
