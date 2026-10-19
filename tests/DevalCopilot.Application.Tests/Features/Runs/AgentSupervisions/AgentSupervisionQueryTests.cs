using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.AgentSupervisions;

/// <summary>ADR-0056 current-state read: the closed status vocabulary and its nullability relationships, attempt selection (the unique
/// running Agent attempt, otherwise the latest), non-disclosing refusal of an unknown run, immediate effective attention before it is
/// persisted, terminal precedence with history, and unavailability instead of an invented fresh heartbeat. It writes nothing.</summary>
public sealed class AgentSupervisionQueryTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(AgentSupervisionTestScene Scene, Guid AttemptId, Guid Observer)> ObservedAsync(
        AgentResponseContract contract = AgentResponseContract.Proposal)
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync(contract);
        return (scene, attemptId, await scene.ObserveAttemptAsync(attemptId));
    }

    [Fact]
    public async Task An_unknown_run_is_a_non_disclosing_not_found()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);

        var result = await scene.ReadAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(AgentSupervisionErrors.NotFoundCode, result.Errors[0].Code);
        Assert.Equal("The requested run was not found.", result.Errors[0].Description);
    }

    [Fact]
    public async Task A_run_without_an_agent_attempt_has_no_attempt_observation_or_attention_even_with_a_simulated_one()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var empty = await scene.ReadValueAsync();
        await scene.ClaimSimulatedAsync();
        var withSimulated = await scene.ReadValueAsync();

        foreach (var value in new[] { empty, withSimulated })
        {
            Assert.Equal(AgentSupervisionStatus.NoAgentAttempt, value.Status);
            Assert.Null(value.Attempt);
            Assert.Null(value.Observation);
            Assert.Null(value.Attention);
            Assert.Null(value.ReasonCode);
            Assert.Equal(AgentSupervisionVocabulary.NoAgentAttemptRecorded, value.NextActionCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_receipt_is_NotObserved_and_never_a_synthesized_heartbeat(bool terminal)
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var attemptId = await scene.ClaimAsync();
        if (terminal)
        {
            await scene.CompleteAsync(attemptId, AttemptStatus.Failed);
        }

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.NotObserved, value.Status);
        Assert.NotNull(value.Attempt);
        Assert.Null(value.Observation);
        Assert.Null(value.Attention);
        Assert.Equal(
            terminal ? AgentSupervisionVocabulary.ReviewTheRecordedResult : AgentSupervisionVocabulary.InspectTheRecordedAttempt,
            value.NextActionCode);
        Assert.Equal(terminal ? AttemptStatus.Failed : AttemptStatus.Running, value.Attempt.Status);
    }

    [Theory]
    [InlineData(AgentResponseContract.Proposal, AgentRole.Planner, AgentProvider.Codex)]
    [InlineData(AgentResponseContract.CriticalReview, AgentRole.CriticalReviewer, AgentProvider.ClaudeCode)]
    [InlineData(AgentResponseContract.ChallengeResolution, AgentRole.Resolver, AgentProvider.Codex)]
    [InlineData(AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentProvider.ClaudeCode)]
    [InlineData(AgentResponseContract.ImplementationReview, AgentRole.CodeReviewer, AgentProvider.Codex)]
    [InlineData(AgentResponseContract.ReviewCorrection, AgentRole.Implementer, AgentProvider.ClaudeCode)]
    [InlineData(AgentResponseContract.VerificationDiagnosis, AgentRole.CodeReviewer, AgentProvider.Codex)]
    public async Task A_fresh_observation_of_every_contract_is_Observed_with_the_exact_attempt_and_receipt(
        AgentResponseContract contract, AgentRole role, AgentProvider provider)
    {
        var (scene, attemptId, observer) = await ObservedAsync(contract);
        scene.Clock.Advance(TimeSpan.FromSeconds(4));
        await scene.HeartbeatAsync(attemptId, observer);
        scene.Clock.Advance(TimeSpan.FromSeconds(1));

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Observed, value.Status);
        Assert.Null(value.ReasonCode);
        Assert.Null(value.Attention);
        Assert.Equal(AgentSupervisionVocabulary.WaitAndRefresh, value.NextActionCode);
        Assert.Equal(scene.Clock.GetUtcNow(), value.ObservedAtUtc);
        var attempt = value.Attempt!;
        Assert.Equal((attemptId, 1, role, provider, contract, AttemptStatus.Running), (attempt.AttemptId, attempt.AttemptNumber, attempt.Role, attempt.Provider, attempt.ResponseContract, attempt.Status));
        Assert.Null(attempt.Outcome);
        Assert.Null(attempt.DispatchedAtUtc);
        var observation = value.Observation!;
        Assert.Equal((scene.Host.Id, observer, true, AgentSupervisionPhase.Preparing), (observation.HostInstanceId, observation.ObserverId, observation.OwnedByCurrentHost, observation.Phase));
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(4), observation.LastHeartbeatAtUtc);
        Assert.Equal(AgentSupervisionTestScene.Start.AddMinutes(2), observation.PhaseDeadlineUtc);
        Assert.Null(observation.ClosedAtUtc);
    }

    [Fact]
    public async Task An_elapsed_deadline_reads_as_attention_immediately_before_anything_persists_it()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        for (var second = 5; second < 120; second += 5)
        {
            scene.Clock.Advance(TimeSpan.FromSeconds(5));
            await scene.HeartbeatAsync(attemptId, observer);
        }

        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        var revision = (await scene.ReceiptAsync(attemptId)).Revision;

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.NeedsAttention, value.Status);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, value.ReasonCode);
        Assert.Equal(AgentSupervisionVocabulary.InspectAndReconcileBeforeFurtherWork, value.NextActionCode);
        var attention = value.Attention!;
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, attention.EffectiveReason);
        Assert.Equal(AgentSupervisionTestScene.Start.AddMinutes(2), attention.EffectiveAtUtc);
        Assert.Null(attention.RecordedReason);
        Assert.Null(attention.RecordedAtUtc);
        var receipt = await scene.ReceiptAsync(attemptId);
        Assert.Equal(revision, receipt.Revision);
        Assert.Null(receipt.AttentionReason);
        // The read persisted nothing: only the registration's own event exists.
        Assert.Equal([RunEventType.AgentSupervisionRegistered], await scene.SupervisionEventTypesAsync());
    }

    [Fact]
    public async Task A_persisted_attention_is_shown_as_effective_and_recorded_and_wins_over_a_later_derivation()
    {
        var (scene, attemptId, _) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(31));
        await scene.SweepAsync((await scene.ReceiptAsync(attemptId)).Id);
        scene.Clock.Advance(TimeSpan.FromMinutes(10));

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.NeedsAttention, value.Status);
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, value.ReasonCode);
        var attention = value.Attention!;
        Assert.Equal(AgentSupervisionAttentionReason.StaleHeartbeat, attention.RecordedReason);
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(30), attention.RecordedAtUtc);
        Assert.Equal(attention.EffectiveAtUtc, attention.RecordedAtUtc);
    }

    [Fact]
    public async Task A_closed_observation_of_a_running_attempt_is_NeedsAttention_with_its_closure()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(2));
        await scene.CloseAsync(attemptId, observer);

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.NeedsAttention, value.Status);
        Assert.Equal(AgentSupervisionAttentionReason.ObserverEndedWithoutTerminalResult, value.ReasonCode);
        Assert.Equal(AgentSupervisionClosureReason.ObserverEnded, value.Observation!.ClosureReason);
        Assert.NotNull(value.Observation.ClosedAtUtc);
        Assert.Equal(AttemptStatus.Running, value.Attempt!.Status);
    }

    [Fact]
    public async Task A_terminal_attempt_governs_with_its_real_status_and_outcome_even_when_the_receipt_is_still_open()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        for (var second = 5; second <= 25; second += 5)
        {
            scene.Clock.Advance(TimeSpan.FromSeconds(5));
            await scene.HeartbeatAsync(attemptId, observer);
        }

        await scene.CompleteAsync(attemptId, AttemptStatus.Completed);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Ended, value.Status);
        Assert.Equal(AttemptStatus.Completed, value.Attempt!.Status);
        Assert.NotNull(value.Attempt.CompletedAtUtc);
        Assert.Null(value.Attention);
        Assert.Null(value.ReasonCode);
        Assert.Equal(AgentSupervisionVocabulary.ReviewTheRecordedResult, value.NextActionCode);
        Assert.NotNull(value.Observation);
    }

    [Fact]
    public async Task A_late_terminal_result_stays_Ended_with_the_missed_deadline_visible_as_history()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        for (var second = 5; second < 120; second += 5)
        {
            scene.Clock.Advance(TimeSpan.FromSeconds(5));
            await scene.HeartbeatAsync(attemptId, observer);
        }

        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        await scene.CompleteAsync(attemptId, AttemptStatus.Completed);
        await scene.CloseAsync(attemptId, observer);

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Ended, value.Status);
        Assert.Equal(AttemptStatus.Completed, value.Attempt!.Status);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, value.Attention!.EffectiveReason);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, value.Attention.RecordedReason);
    }

    [Fact]
    public async Task The_running_attempt_is_shown_and_without_a_running_one_the_latest_attempt_number_is()
    {
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        var first = await scene.ClaimAsync();
        await scene.CompleteAsync(first, AttemptStatus.Failed);
        var second = await scene.ClaimAsync(AgentResponseContract.CriticalReview);

        var whileRunning = await scene.ReadValueAsync();
        await scene.CompleteAsync(second, AttemptStatus.Completed);
        var afterwards = await scene.ReadValueAsync();

        Assert.Equal(second, whileRunning.Attempt!.AttemptId);
        Assert.Equal(AttemptStatus.Running, whileRunning.Attempt.Status);
        Assert.Equal(second, afterwards.Attempt!.AttemptId);
        Assert.Equal(2, afterwards.Attempt.AttemptNumber);
        Assert.Equal(AttemptStatus.Completed, afterwards.Attempt.Status);
    }

    [Fact]
    public async Task Several_running_agent_attempts_are_Unavailable_never_an_arbitrary_winner()
    {
        // The database normally forbids this (one Running attempt per run); the read still refuses to pick a winner if it ever happens.
        var scene = await AgentSupervisionTestScene.CreateAsync(fixture);
        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("DROP INDEX ix_attempts_run_id_one_running;");
        }

        await scene.ObserveAttemptAsync(await scene.ClaimAsync());
        await scene.ClaimAsync(AgentResponseContract.CriticalReview);

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Unavailable, value.Status);
        Assert.Equal(AgentSupervisionVocabulary.MultipleActiveAgentAttempts, value.ReasonCode);
        Assert.Null(value.Attempt);
        Assert.Null(value.Observation);
        Assert.Null(value.Attention);
    }

    [Fact]
    public async Task An_incoherent_attempt_contract_is_Unavailable()
    {
        var (scene, attemptId, _) = await ObservedAsync();
        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE attempts SET \"AgentRole\" = 'Resolver' WHERE \"Id\" = {0}", attemptId.ToString().ToUpperInvariant());
        }

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Unavailable, value.Status);
        Assert.Equal(AgentSupervisionVocabulary.AttemptFactsIncoherent, value.ReasonCode);
    }

    [Fact]
    public async Task A_closed_observation_of_a_running_attempt_without_any_attention_is_incoherent_not_healthy()
    {
        var (scene, attemptId, _) = await ObservedAsync();
        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER trg_agent_supervision_immutable;");
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE agent_supervisions SET \"ClosureReason\" = 'attempt_terminal', \"ClosedAtUtc\" = \"StartedAtUtc\" WHERE \"AttemptId\" = {0}",
                attemptId.ToString().ToUpperInvariant());
        }

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Unavailable, value.Status);
        Assert.Equal(AgentSupervisionVocabulary.SupervisionFactsIncoherent, value.ReasonCode);
        Assert.NotNull(value.Attempt);
        Assert.NotNull(value.Observation);
    }

    [Fact]
    public async Task An_old_hosts_observation_is_never_accepted_as_fresh_by_a_later_launch()
    {
        var (scene, _, _) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(1));

        var value = (await scene.ReadAsync(host: new AgentSupervisionHostInstance(Guid.NewGuid()))).Value;

        Assert.Equal(AgentSupervisionStatus.NeedsAttention, value.Status);
        Assert.Equal(AgentSupervisionAttentionReason.HostInterrupted, value.ReasonCode);
        Assert.False(value.Observation!.OwnedByCurrentHost);
    }

    [Fact]
    public async Task A_heartbeat_committed_while_the_read_runs_is_a_fresh_report_never_an_incoherent_chronology()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(5));
        await scene.HeartbeatAsync(attemptId, observer);

        // The read's first clock sample precedes the heartbeat that committed meanwhile; the judging sample follows the receipt read.
        var steppedClock = new SteppedClock(AgentSupervisionTestScene.Start, TimeSpan.FromSeconds(10));
        await using var context = fixture.CreateContext();
        var result = await new Application.Features.Runs.Queries.GetAgentSupervision.GetAgentSupervisionQueryHandler(
            context, steppedClock, scene.Host).HandleAsync(new Application.Features.Runs.Queries.GetAgentSupervision.GetAgentSupervisionQuery(scene.RunId), CancellationToken.None);

        Assert.Equal(AgentSupervisionStatus.Observed, result.Value.Status);
        Assert.Equal(AgentSupervisionTestScene.Start.AddSeconds(10), result.Value.ObservedAtUtc);
    }

    private sealed class SteppedClock(DateTimeOffset first, TimeSpan step) : TimeProvider
    {
        private int calls;

        public override DateTimeOffset GetUtcNow() => first + (step * calls++);
    }

    [Fact]
    public async Task A_backward_clock_is_Unavailable_never_fresh()
    {
        var (scene, attemptId, observer) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(10));
        await scene.HeartbeatAsync(attemptId, observer);
        scene.Clock.Advance(TimeSpan.FromSeconds(-5));

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Unavailable, value.Status);
        Assert.Equal(AgentSupervisionVocabulary.ChronologyInvalid, value.ReasonCode);
        Assert.Null(value.Attention);
    }

    [Fact]
    public async Task Reading_writes_nothing_at_all()
    {
        var (scene, attemptId, _) = await ObservedAsync();
        scene.Clock.Advance(TimeSpan.FromMinutes(10));
        var factsBefore = await scene.FactsAsync();
        var receiptBefore = await scene.ReceiptAsync(attemptId);

        await scene.ReadValueAsync();
        await scene.ReadValueAsync();

        Assert.Equal(factsBefore, await scene.FactsAsync());
        var receiptAfter = await scene.ReceiptAsync(attemptId);
        Assert.Equal((receiptBefore.Revision, receiptBefore.AttentionReason, receiptBefore.ClosedAtUtc), (receiptAfter.Revision, receiptAfter.AttentionReason, receiptAfter.ClosedAtUtc));
    }

    [Fact]
    public async Task A_dispatch_marker_is_reported_as_dispatched_but_never_as_proof_of_a_process()
    {
        var (scene, attemptId, _) = await ObservedAsync();
        await using (var context = fixture.CreateContext())
        {
            var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
            attempt.MarkAgentDispatched(scene.Clock.GetUtcNow());
            await context.SaveChangesAsync();
        }

        var value = await scene.ReadValueAsync();

        Assert.Equal(AgentSupervisionStatus.Observed, value.Status);
        Assert.Equal(scene.Clock.GetUtcNow(), value.Attempt!.DispatchedAtUtc);
    }
}
