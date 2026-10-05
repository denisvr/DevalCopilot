using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordCodexAccountUsageStop;
using DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageStopPlan;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.AccountUsageStopTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The dispatch seam of the Codex account-usage stop (ADR-0025) on real file-backed SQLite: the last gate
/// (<see cref="MarkAgentAttemptDispatchedCommandHandler"/>) never commits the dispatch marker for a threshold-bearing Codex attempt
/// without its own fresh, matching guard facts, whatever a caller omits or changes; the dedicated command resolves a claimed,
/// undispatched attempt atomically to its one terminal outcome and refuses to stop an attempt the policy permits.
/// </summary>
public sealed class CodexAccountUsageDispatchSeamTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private const string Executable = @"C:\safe\codex.exe";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed record Seed(Guid RunId, Guid AttemptId);

    private async Task<Seed> SeedAsync(int? snapshot = 80, bool launchTarget = true)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        if (snapshot is { } threshold)
        {
            attempt.SnapshotCodexAccountUsageStop(threshold);
        }

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.RepositoryMutationLeases.Add(lease);
        context.Attempts.Add(attempt);
        if (launchTarget && !await context.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.CodexCli))
        {
            var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
            codex.MarkDispatched(Now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, Executable, null, "1.2.3", Now, Now.AddMinutes(5));
            context.HostCapabilitySnapshots.Add(codex);
        }

        await context.SaveChangesAsync();
        return new Seed(run.Id, attempt.Id);
    }

    private static CodexAccountUsageGuardFacts Facts(
        Seed seed, int threshold = 80, AccountUsageObservation? observation = null, string exe = Executable, Guid? attemptId = null) =>
        new(attemptId ?? seed.AttemptId, threshold, exe, null, observation ?? Observation(Now, 10, 5), Now, Now);

    private async Task<(bool Success, string? Code)> MarkAsync(Seed seed, CodexAccountUsageGuardFacts? facts, TimeProvider? clock = null)
    {
        await using var context = _fixture.CreateContext();
        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, clock ?? new AdjustableTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(seed.RunId, seed.AttemptId, ExpectedAccountUsageGuard: facts), CancellationToken.None);
        if (result.IsSuccess)
        {
            await context.SaveChangesAsync();
        }

        return (result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null);
    }

    private async Task<Attempt> AttemptAsync(Seed seed)
    {
        await using var context = _fixture.CreateContext();
        return await context.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.AttemptId);
    }

    private async Task<(bool Success, string? Code, RecordCodexAccountUsageStopCommandResult? Value)> RecordAsync(
        Seed seed, CodexAccountUsageGuardFacts? facts, TimeProvider? clock = null)
    {
        await using var context = _fixture.CreateContext();
        var result = await new RecordCodexAccountUsageStopCommandHandler(context, clock ?? new AdjustableTimeProvider(Now)).HandleAsync(
            new RecordCodexAccountUsageStopCommand(seed.RunId, seed.AttemptId, facts), CancellationToken.None);
        return (result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null, result.IsSuccess ? result.Value : null);
    }

    // ---- the last gate -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_attempt_without_a_snapshot_dispatches_exactly_as_before_and_needs_no_facts()
    {
        var seed = await SeedAsync(snapshot: null);

        var result = await MarkAsync(seed, null);

        Assert.True(result.Success, result.Code);
        Assert.NotNull((await AttemptAsync(seed)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task An_attempt_without_a_snapshot_is_never_dispatched_with_facts()
    {
        var seed = await SeedAsync(snapshot: null);

        var result = await MarkAsync(seed, Facts(seed));

        Assert.Equal(RecordCodexAccountUsageStopCommandHandler.GuardMismatchCode, result.Code);
        Assert.Null((await AttemptAsync(seed)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task A_threshold_bearing_attempt_is_never_dispatched_without_facts()
    {
        var seed = await SeedAsync();

        var result = await MarkAsync(seed, null);

        Assert.Equal(CodexAccountUsageStopGate.EvidenceUnavailableCode, result.Code);
        Assert.Null((await AttemptAsync(seed)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task Matching_fresh_facts_below_the_threshold_dispatch_once()
    {
        var seed = await SeedAsync();

        var first = await MarkAsync(seed, Facts(seed));
        var second = await MarkAsync(seed, Facts(seed));

        Assert.True(first.Success, first.Code);
        Assert.Equal("attempts.already_dispatched", second.Code);
    }

    [Theory]
    [InlineData(80, 0)]
    [InlineData(10, 80)]
    [InlineData(100, 100)]
    public async Task Facts_at_or_above_the_threshold_never_commit_the_marker(int primary, int secondary)
    {
        var seed = await SeedAsync();

        var result = await MarkAsync(seed, Facts(seed, observation: Observation(Now, primary, secondary)));

        Assert.Equal(CodexAccountUsageStopGate.ReachedCode, result.Code);
        Assert.Null((await AttemptAsync(seed)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task A_provider_reached_state_never_commits_the_marker()
    {
        var seed = await SeedAsync();

        var result = await MarkAsync(seed, Facts(seed, observation: Observation(Now, 1, providerReached: true)));

        Assert.Equal(CodexAccountUsageStopGate.ReachedCode, result.Code);
    }

    [Fact]
    public async Task An_unavailable_observation_never_commits_the_marker()
    {
        var seed = await SeedAsync();

        var result = await MarkAsync(seed, Facts(seed, observation: AccountUsageObservation.Unavailable));

        Assert.Equal(CodexAccountUsageStopGate.EvidenceUnavailableCode, result.Code);
        Assert.Null((await AttemptAsync(seed)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task Facts_bound_to_another_attempt_threshold_or_launch_tuple_never_commit_the_marker()
    {
        var seed = await SeedAsync();
        var other = await SeedAsync();

        var otherAttempt = await MarkAsync(seed, Facts(seed, attemptId: other.AttemptId));
        var noAttempt = await MarkAsync(seed, Facts(seed) with { AttemptId = null });
        var otherThreshold = await MarkAsync(seed, Facts(seed, threshold: 100));
        var otherTuple = await MarkAsync(seed, Facts(seed, exe: @"C:\safe\other-codex.exe"));
        var otherScript = await MarkAsync(seed, Facts(seed) with { ScriptPath = @"C:\safe\codex.js" });

        Assert.All(
            new[] { otherAttempt, noAttempt, otherThreshold, otherTuple, otherScript },
            outcome => Assert.Equal(RecordCodexAccountUsageStopCommandHandler.GuardMismatchCode, outcome.Code));
        Assert.Null((await AttemptAsync(seed)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task A_launch_target_that_is_no_longer_vetted_never_commits_the_marker()
    {
        var seed = await SeedAsync(launchTarget: false);

        var result = await MarkAsync(seed, Facts(seed));

        Assert.Equal(RecordCodexAccountUsageStopCommandHandler.GuardMismatchCode, result.Code);
    }

    [Fact]
    public async Task Evidence_older_than_thirty_seconds_at_the_gate_never_commits_the_marker()
    {
        var seed = await SeedAsync();

        var fresh = await MarkAsync(seed, Facts(seed), new AdjustableTimeProvider(Now.AddSeconds(30)));
        var seed2 = await SeedAsync();
        var stale = await MarkAsync(seed2, Facts(seed2), new AdjustableTimeProvider(Now.AddSeconds(31)));

        Assert.True(fresh.Success, fresh.Code);
        Assert.Equal(CodexAccountUsageStopGate.EvidenceUnavailableCode, stale.Code);
        Assert.Null((await AttemptAsync(seed2)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task A_malformed_stored_snapshot_is_never_dispatched_with_or_without_facts()
    {
        var seed = await SeedAsync();
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentCodexAccountUsageStopPercent = {"abc"} WHERE Id = {seed.AttemptId}");
        }

        var without = await MarkAsync(seed, null);
        var with = await MarkAsync(seed, Facts(seed));

        Assert.Equal(CodexAccountUsageStopGate.SettingInvalidCode, without.Code);
        Assert.Equal(CodexAccountUsageStopGate.SettingInvalidCode, with.Code);
        Assert.Null((await AttemptAsync(seed)).AgentDispatchedAtUtc);
    }

    // ---- the plan the supervisors read -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, CodexAccountUsageStopPlanState.NotConfigured, null)]
    [InlineData(80, CodexAccountUsageStopPlanState.Threshold, 80)]
    public async Task The_plan_reads_the_attempts_own_snapshot(int? snapshot, CodexAccountUsageStopPlanState state, int? threshold)
    {
        var seed = await SeedAsync(snapshot);
        await using var context = _fixture.CreateContext();

        var plan = await new GetCodexAccountUsageStopPlanQueryHandler(context).HandleAsync(
            new GetCodexAccountUsageStopPlanQuery(seed.RunId, seed.AttemptId), CancellationToken.None);

        Assert.Equal(new GetCodexAccountUsageStopPlanQueryResult(state, threshold), plan);
    }

    [Fact]
    public async Task The_plan_for_a_malformed_snapshot_is_invalid_and_for_an_unknown_attempt_not_configured()
    {
        var seed = await SeedAsync();
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentCodexAccountUsageStopPercent = {3.5} WHERE Id = {seed.AttemptId}");
        }

        await using var read = _fixture.CreateContext();
        var handler = new GetCodexAccountUsageStopPlanQueryHandler(read);
        var invalid = await handler.HandleAsync(new GetCodexAccountUsageStopPlanQuery(seed.RunId, seed.AttemptId), CancellationToken.None);
        var unknown = await handler.HandleAsync(new GetCodexAccountUsageStopPlanQuery(seed.RunId, Guid.NewGuid()), CancellationToken.None);
        var wrongRun = await handler.HandleAsync(new GetCodexAccountUsageStopPlanQuery(Guid.NewGuid(), seed.AttemptId), CancellationToken.None);

        Assert.Equal(CodexAccountUsageStopPlanState.Invalid, invalid.State);
        Assert.Null(invalid.ThresholdPercent);
        Assert.Equal(CodexAccountUsageStopPlanState.NotConfigured, unknown.State);
        Assert.Equal(CodexAccountUsageStopPlanState.NotConfigured, wrongRun.State);
    }

    // ---- the dedicated terminal command ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_reached_guard_resolves_the_attempt_atomically_with_its_decision_and_one_completion_event()
    {
        var seed = await SeedAsync();

        var result = await RecordAsync(seed, Facts(seed, observation: Observation(Now, 80, 3)));

        Assert.True(result.Success, result.Code);
        Assert.Equal(AttemptStatus.Failed, result.Value!.Status);
        Assert.Equal(AgentOutcome.AccountUsageStopReached, result.Value.Outcome);
        Assert.True(result.Value.LatestEventSequence > 0);
        var attempt = await AttemptAsync(seed);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.AccountUsageStopReached, attempt.AgentOutcome);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.NotNull(attempt.CompletedAtUtc);
        var decision = attempt.GetAgentAccountUsageDecision()!;
        Assert.Equal(CodexAccountUsageDecisionReason.ThresholdReached, decision.Reason);
        Assert.Equal(80, decision.ThresholdPercent);
        Assert.Equal(Now, decision.RetrievedAtUtc);
        Assert.Equal([80, 3], decision.Windows.Select(window => window.UsedPercent));
        Assert.Null(attempt.GetAgentTokenUsageEvidence());
        Assert.Null(attempt.GetAgentModelContextLimitsEvidence());

        await using var verify = _fixture.CreateContext();
        var completion = Assert.Single(await verify.Events.AsNoTracking().Where(e => e.AttemptId == seed.AttemptId).ToListAsync());
        Assert.Equal(RunEventType.AgentAttemptCompleted, completion.EventType);
        Assert.Equal(result.Value.LatestEventSequence, completion.Sequence);
        Assert.Equal("{\"status\":\"Failed\",\"outcome\":\"AccountUsageStopReached\"}", completion.PayloadJson);
        Assert.Empty(await verify.CollaborationMessages.AsNoTracking().Where(m => m.RunId == seed.RunId).ToListAsync());
    }

    [Fact]
    public async Task No_facts_for_a_threshold_bearing_attempt_resolve_it_as_evidence_unavailable()
    {
        var seed = await SeedAsync();

        var result = await RecordAsync(seed, null);

        Assert.True(result.Success, result.Code);
        Assert.Equal(AgentOutcome.AccountUsageEvidenceUnavailable, result.Value!.Outcome);
        var decision = (await AttemptAsync(seed)).GetAgentAccountUsageDecision()!;
        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceUnavailable, decision.Reason);
        Assert.Empty(decision.Windows);
    }

    [Fact]
    public async Task Facts_that_have_gone_stale_resolve_as_expired_evidence()
    {
        var seed = await SeedAsync();

        var result = await RecordAsync(seed, Facts(seed), new AdjustableTimeProvider(Now.AddSeconds(31)));

        Assert.True(result.Success, result.Code);
        var decision = (await AttemptAsync(seed)).GetAgentAccountUsageDecision()!;
        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceExpired, decision.Reason);
        Assert.Equal(Now, decision.RetrievedAtUtc);
    }

    [Fact]
    public async Task Facts_made_against_another_launch_tuple_resolve_as_evidence_unavailable()
    {
        var seed = await SeedAsync();

        var result = await RecordAsync(seed, Facts(seed, exe: @"C:\safe\other-codex.exe", observation: Observation(Now, 99)));

        Assert.True(result.Success, result.Code);
        Assert.Equal(AgentOutcome.AccountUsageEvidenceUnavailable, result.Value!.Outcome);
        Assert.Empty((await AttemptAsync(seed)).GetAgentAccountUsageDecision()!.Windows);
    }

    [Fact]
    public async Task A_malformed_threshold_snapshot_resolves_without_a_guessed_number()
    {
        var seed = await SeedAsync();
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentCodexAccountUsageStopPercent = {"blob:37"} WHERE Id = {seed.AttemptId}");
        }

        var result = await RecordAsync(seed, null);

        Assert.True(result.Success, result.Code);
        var decision = (await AttemptAsync(seed)).GetAgentAccountUsageDecision()!;
        Assert.Equal(CodexAccountUsageDecisionReason.ThresholdUnusable, decision.Reason);
        Assert.Null(decision.ThresholdPercent);
    }

    [Fact]
    public async Task A_guard_that_permits_the_attempt_cannot_be_used_to_stop_it()
    {
        var seed = await SeedAsync();

        var result = await RecordAsync(seed, Facts(seed));

        Assert.Equal(RecordCodexAccountUsageStopCommandHandler.NotApplicableCode, result.Code);
        var attempt = await AttemptAsync(seed);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentAccountUsageDecisionSnapshot);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(await verify.Events.AsNoTracking().Where(e => e.AttemptId == seed.AttemptId).ToListAsync());
    }

    [Fact]
    public async Task Facts_that_belong_to_another_attempt_are_refused_without_a_change()
    {
        var seed = await SeedAsync();
        var other = await SeedAsync();

        var otherAttempt = await RecordAsync(seed, Facts(seed, attemptId: other.AttemptId, observation: Observation(Now, 99)));

        Assert.Equal(RecordCodexAccountUsageStopCommandHandler.GuardMismatchCode, otherAttempt.Code);
        Assert.Equal(AttemptStatus.Running, (await AttemptAsync(seed)).Status);
    }

    [Fact]
    public async Task An_attempt_that_claimed_no_stop_cannot_record_one()
    {
        var seed = await SeedAsync(snapshot: null);

        var result = await RecordAsync(seed, null);

        Assert.Equal(RecordCodexAccountUsageStopCommandHandler.NotClaimedCode, result.Code);
        Assert.Equal(AttemptStatus.Running, (await AttemptAsync(seed)).Status);
    }

    [Fact]
    public async Task A_dispatched_a_completed_and_an_unknown_attempt_cannot_record_a_stop()
    {
        var dispatched = await SeedAsync();
        Assert.True((await MarkAsync(dispatched, Facts(dispatched))).Success);
        var stopped = await SeedAsync();
        Assert.True((await RecordAsync(stopped, null)).Success);

        var afterDispatch = await RecordAsync(dispatched, Facts(dispatched, observation: Observation(Now, 99)));
        var twice = await RecordAsync(stopped, null);
        var unknown = await RecordAsync(new Seed(dispatched.RunId, Guid.NewGuid()), null);
        var wrongRun = await RecordAsync(new Seed(Guid.NewGuid(), stopped.AttemptId), null);

        Assert.Equal("attempts.not_eligible", afterDispatch.Code);
        Assert.Equal("attempts.not_eligible", twice.Code);
        Assert.Equal("attempts.not_found", unknown.Code);
        Assert.Equal("attempts.not_found", wrongRun.Code);
        Assert.Null((await AttemptAsync(dispatched)).AgentOutcome);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.Events.AsNoTracking().Where(e => e.AttemptId == stopped.AttemptId).ToListAsync());
    }

    [Fact]
    public async Task A_stopped_attempt_is_never_eligible_for_dispatch_again()
    {
        var seed = await SeedAsync();
        Assert.True((await RecordAsync(seed, Facts(seed, observation: Observation(Now, 90)))).Success);

        var again = await MarkAsync(seed, Facts(seed));

        Assert.Equal("attempts.not_active", again.Code);
        Assert.Null((await AttemptAsync(seed)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task The_recorded_decision_is_the_only_provider_free_text_the_event_carries()
    {
        var seed = await SeedAsync();
        await RecordAsync(seed, Facts(seed, observation: Observation(Now, 90)));

        await using var verify = _fixture.CreateContext();
        var completion = await verify.Events.AsNoTracking().SingleAsync(e => e.AttemptId == seed.AttemptId);
        using var payload = JsonDocument.Parse(completion.PayloadJson);

        Assert.Equal(["status", "outcome"], payload.RootElement.EnumerateObject().Select(member => member.Name));
    }

    // ---- R1: fresh persisted authority at terminal recording ----------------------------------------------------------------------

    private async Task ChangeSnapshotAsync(Seed seed, int percent)
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attempts SET AgentCodexAccountUsageStopPercent = {CodexAccountUsageStop.Format(percent)} WHERE Id = {seed.AttemptId}");
    }

    [Fact]
    public async Task A_snapshot_changed_after_preparation_is_resolved_as_unavailable_for_the_actual_snapshot_and_never_dispatches_later()
    {
        var seed = await SeedAsync();
        var prepared = Facts(seed, threshold: 80);
        await ChangeSnapshotAsync(seed, 90);

        var gate = await MarkAsync(seed, prepared);
        var recorded = await RecordAsync(seed, prepared);

        Assert.Equal(RecordCodexAccountUsageStopCommandHandler.GuardMismatchCode, gate.Code);
        Assert.True(recorded.Success, recorded.Code);
        Assert.Equal(AgentOutcome.AccountUsageEvidenceUnavailable, recorded.Value!.Outcome);
        var attempt = await AttemptAsync(seed);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        var decision = attempt.GetAgentAccountUsageDecision()!;
        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceUnavailable, decision.Reason);
        Assert.Equal(90, decision.ThresholdPercent);
        Assert.Empty(decision.Windows);

        var later = await MarkAsync(seed, Facts(seed, threshold: 90));
        Assert.Equal("attempts.not_active", later.Code);
        Assert.Null((await AttemptAsync(seed)).AgentDispatchedAtUtc);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.Events.AsNoTracking().Where(e => e.AttemptId == seed.AttemptId).ToListAsync());
    }

    [Fact]
    public async Task Recording_with_a_populated_tracker_uses_the_fresh_snapshot_not_the_tracked_entity()
    {
        var seed = await SeedAsync();
        await using var context = _fixture.CreateContext();
        var tracked = await context.Attempts.SingleAsync(candidate => candidate.Id == seed.AttemptId);
        Assert.Equal(80, tracked.ReadAgentCodexAccountUsageStopPercent().Value);
        await ChangeSnapshotAsync(seed, 90);

        var result = await new RecordCodexAccountUsageStopCommandHandler(context, new AdjustableTimeProvider(Now)).HandleAsync(
            new RecordCodexAccountUsageStopCommand(seed.RunId, seed.AttemptId, Facts(seed, threshold: 90, observation: Observation(Now, 95))),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        Assert.Equal(AgentOutcome.AccountUsageStopReached, result.Value.Outcome);
        var attempt = await AttemptAsync(seed);
        Assert.Equal(90, attempt.GetAgentAccountUsageDecision()!.ThresholdPercent);
    }

    [Fact]
    public async Task Recording_with_a_populated_tracker_does_not_trust_a_stale_dispatch_marker_or_status()
    {
        var seed = await SeedAsync();
        await using var context = _fixture.CreateContext();
        await context.Attempts.SingleAsync(candidate => candidate.Id == seed.AttemptId);
        Assert.True((await MarkAsync(seed, Facts(seed))).Success);

        var result = await new RecordCodexAccountUsageStopCommandHandler(context, new AdjustableTimeProvider(Now)).HandleAsync(
            new RecordCodexAccountUsageStopCommand(seed.RunId, seed.AttemptId, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_eligible", result.Errors[0].Code);
        Assert.Null((await AttemptAsync(seed)).AgentOutcome);
    }

    [Fact]
    public async Task Facts_for_another_threshold_resolve_as_unavailable_but_facts_for_another_attempt_never_acquire_authority()
    {
        var seed = await SeedAsync();
        var other = await SeedAsync();

        var wrongAttempt = await RecordAsync(seed, Facts(seed, attemptId: other.AttemptId, observation: Observation(Now, 99)));
        Assert.Equal(RecordCodexAccountUsageStopCommandHandler.GuardMismatchCode, wrongAttempt.Code);
        Assert.Equal(AttemptStatus.Running, (await AttemptAsync(seed)).Status);

        var otherThreshold = await RecordAsync(seed, Facts(seed, threshold: 50, observation: Observation(Now, 99)));
        Assert.True(otherThreshold.Success, otherThreshold.Code);
        Assert.Equal(AgentOutcome.AccountUsageEvidenceUnavailable, otherThreshold.Value!.Outcome);
        var decision = (await AttemptAsync(seed)).GetAgentAccountUsageDecision()!;
        Assert.Equal(80, decision.ThresholdPercent);
        Assert.Empty(decision.Windows);
        Assert.Equal(AttemptStatus.Running, (await AttemptAsync(other)).Status);
    }

    [Fact]
    public async Task The_recorder_holds_one_short_explicit_transaction_and_commits_the_event_with_the_decision()
    {
        var seed = await SeedAsync();

        var result = await RecordAsync(seed, Facts(seed, observation: Observation(Now, 90)));

        Assert.True(result.Success, result.Code);
        Assert.IsAssignableFrom<Devalente.Shared.Cqrs.IManualTransactionCommand<Devalente.Shared.Results.Result<RecordCodexAccountUsageStopCommandResult>>>(
            new RecordCodexAccountUsageStopCommand(seed.RunId, seed.AttemptId, null));
        await using var verify = _fixture.CreateContext();
        var completion = Assert.Single(await verify.Events.AsNoTracking().Where(e => e.AttemptId == seed.AttemptId).ToListAsync());
        Assert.Equal(result.Value!.LatestEventSequence, completion.Sequence);
    }
}
