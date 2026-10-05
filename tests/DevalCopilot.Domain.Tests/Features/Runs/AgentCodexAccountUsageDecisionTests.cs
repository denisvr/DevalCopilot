using System.Reflection;
using System.Text;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>The recorded Codex account-usage stop decision (ADR-0025): a bounded canonical snapshot with an immutable copy of the
/// windows it used, strict reconstruction that trusts only the exact canonical text, and the atomic terminal attempt transition that
/// records it. The attempt's stored threshold and the decision must agree.</summary>
public sealed class AgentCodexAccountUsageDecisionTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly string Canonical =
        "{\"version\":1,\"source\":\"codex-account-rate-limits-v1\",\"decision\":\"reached\",\"reason\":\"threshold_reached\","
        + "\"thresholdPercent\":80,\"retrievedAtUtc\":\"2026-10-04T12:00:00.0000000Z\","
        + "\"windows\":[{\"bucket\":\"codex\",\"window\":\"primary\",\"usedPercent\":80},{\"bucket\":\"codex\",\"window\":\"secondary\",\"usedPercent\":10}]}";

    private static AgentCodexAccountUsageDecision Reached(int threshold = 80, params CodexAccountUsageWindowFact[] windows) =>
        AgentCodexAccountUsageDecision.Create(
            CodexAccountUsageDecisionKind.Reached,
            CodexAccountUsageDecisionReason.ThresholdReached,
            threshold,
            BaseTime,
            windows.Length == 0
                ? [new("codex", CodexAccountUsageWindowKind.Secondary, 10), new("codex", CodexAccountUsageWindowKind.Primary, 80)]
                : windows);

    private static AgentCodexAccountUsageDecision Unavailable(
        CodexAccountUsageDecisionReason reason = CodexAccountUsageDecisionReason.EvidenceUnavailable, int? threshold = 80, DateTimeOffset? at = null) =>
        AgentCodexAccountUsageDecision.Create(CodexAccountUsageDecisionKind.Unavailable, reason, threshold, at, []);

    private static Attempt ClaimCodex(int? snapshot)
    {
        var attempt = Attempt.ClaimAgentWithAssignment(
            Guid.NewGuid(), Guid.NewGuid(), attemptNumber: 1, Guid.NewGuid(), Guid.NewGuid(),
            "fingerprint-1", Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, BaseTime,
            requestedModel: null, requestedEffort: null, agentBudgetSlot: 1);
        if (snapshot is { } value)
        {
            attempt.SnapshotCodexAccountUsageStop(value);
        }

        return attempt;
    }

    private static void SetStored(object target, string field, string? value) =>
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    [Fact]
    public void A_reached_decision_serializes_to_the_one_canonical_text_with_ordered_windows()
    {
        Assert.Equal(Canonical, Reached().Serialize());
    }

    [Fact]
    public void The_canonical_text_round_trips_exactly()
    {
        var decision = AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, Canonical);

        Assert.NotNull(decision);
        Assert.Equal(CodexAccountUsageDecisionKind.Reached, decision.Kind);
        Assert.Equal(CodexAccountUsageDecisionReason.ThresholdReached, decision.Reason);
        Assert.Equal(80, decision.ThresholdPercent);
        Assert.Equal(BaseTime, decision.RetrievedAtUtc);
        Assert.Equal(Canonical, decision.Serialize());
    }

    [Fact]
    public void Equality_at_the_threshold_is_a_valid_reached_decision_and_a_lower_window_alone_is_not()
    {
        var equal = AgentCodexAccountUsageDecision.Create(
            CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, 80, BaseTime,
            [new(null, CodexAccountUsageWindowKind.Primary, 80)]);
        Assert.Equal(80, Assert.Single(equal.Windows).UsedPercent);

        Assert.Throws<ArgumentException>(() => AgentCodexAccountUsageDecision.Create(
            CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, 80, BaseTime,
            [new(null, CodexAccountUsageWindowKind.Primary, 79)]));
    }

    [Fact]
    public void A_provider_reported_reached_state_is_a_reached_decision_even_below_the_threshold()
    {
        var decision = AgentCodexAccountUsageDecision.Create(
            CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ProviderReportedLimitReached, 80, BaseTime,
            [new("codex", CodexAccountUsageWindowKind.Primary, 5)]);

        Assert.Equal(CodexAccountUsageDecisionKind.Reached, decision.Kind);
        Assert.Contains("\"reason\":\"provider_limit_reached\"", decision.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_legacy_bucket_without_an_identifier_is_written_as_null_and_ordered_first()
    {
        var decision = AgentCodexAccountUsageDecision.Create(
            CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, 50, BaseTime,
            [new("zeta", CodexAccountUsageWindowKind.Primary, 60), new(null, CodexAccountUsageWindowKind.Primary, 70)]);

        Assert.Null(decision.Windows[0].BucketId);
        Assert.StartsWith("{\"version\":1", decision.Serialize(), StringComparison.Ordinal);
        Assert.Contains("\"windows\":[{\"bucket\":null,", decision.Serialize(), StringComparison.Ordinal);
        Assert.Equal(decision.Serialize(), AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, decision.Serialize())!.Serialize());
    }

    [Theory]
    [InlineData(CodexAccountUsageDecisionReason.EvidenceUnavailable)]
    [InlineData(CodexAccountUsageDecisionReason.EvidenceExpired)]
    public void An_unavailable_decision_with_a_valid_threshold_carries_no_windows(CodexAccountUsageDecisionReason reason)
    {
        var decision = Unavailable(reason, 80, reason == CodexAccountUsageDecisionReason.EvidenceExpired ? BaseTime : null);

        Assert.Empty(decision.Windows);
        Assert.Equal(decision.Serialize(), AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, decision.Serialize())!.Serialize());
    }

    [Fact]
    public void An_unusable_threshold_decision_has_no_number_and_no_observation()
    {
        var decision = Unavailable(CodexAccountUsageDecisionReason.ThresholdUnusable, threshold: null);

        Assert.Null(decision.ThresholdPercent);
        Assert.Contains("\"thresholdPercent\":null", decision.Serialize(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.EvidenceUnavailable)]
    [InlineData(CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdUnusable)]
    [InlineData(CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.ThresholdReached)]
    [InlineData(CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.ProviderReportedLimitReached)]
    public void A_decision_and_a_reason_that_disagree_are_refused(CodexAccountUsageDecisionKind kind, CodexAccountUsageDecisionReason reason)
    {
        Assert.Throws<ArgumentException>(() => AgentCodexAccountUsageDecision.Create(
            kind, reason, 80, BaseTime, [new("codex", CodexAccountUsageWindowKind.Primary, 90)]));
    }

    [Fact]
    public void Shape_violations_are_refused_without_a_partial_decision()
    {
        var ok = new CodexAccountUsageWindowFact("codex", CodexAccountUsageWindowKind.Primary, 90);
        void Refused(int? threshold, DateTimeOffset? at, params CodexAccountUsageWindowFact[] windows) =>
            Assert.Throws<ArgumentException>(() => AgentCodexAccountUsageDecision.Create(
                CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, threshold, at, windows));

        Refused(null, BaseTime, ok);
        Refused(0, BaseTime, ok);
        Refused(101, BaseTime, ok);
        Refused(80, null, ok);
        Refused(80, BaseTime);
        Refused(80, BaseTime, ok, ok);
        Refused(80, BaseTime, new CodexAccountUsageWindowFact("codex", CodexAccountUsageWindowKind.Primary, 101));
        Refused(80, BaseTime, new CodexAccountUsageWindowFact("codex", CodexAccountUsageWindowKind.Primary, -1));
        Refused(80, BaseTime, new CodexAccountUsageWindowFact("bad id", CodexAccountUsageWindowKind.Primary, 90));
        Refused(80, BaseTime, new CodexAccountUsageWindowFact(new string('a', 65), CodexAccountUsageWindowKind.Primary, 90));
        Refused(80, BaseTime, new CodexAccountUsageWindowFact("codex", (CodexAccountUsageWindowKind)9, 90));
        Refused(80, BaseTime, null!);
    }

    [Fact]
    public void The_cardinality_bounds_are_enforced_inclusively_and_the_result_stays_below_the_size_bound()
    {
        var sixteen = Enumerable.Range(0, 16).Select(index => new CodexAccountUsageWindowFact($"b{index:D2}", CodexAccountUsageWindowKind.Primary, 90));
        var sixteenBoth = sixteen.Concat(Enumerable.Range(0, 16).Select(index => new CodexAccountUsageWindowFact($"b{index:D2}", CodexAccountUsageWindowKind.Secondary, 90))).ToArray();
        var max = AgentCodexAccountUsageDecision.Create(
            CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, 80, BaseTime, sixteenBoth);

        Assert.Equal(32, max.Windows.Length);
        Assert.True(Encoding.UTF8.GetByteCount(max.Serialize()) < AgentCodexAccountUsageDecision.MaxSerializedBytes);

        var seventeenBuckets = sixteenBoth.Append(new CodexAccountUsageWindowFact("b99", CodexAccountUsageWindowKind.Primary, 90)).ToArray();
        Assert.Throws<ArgumentException>(() => AgentCodexAccountUsageDecision.Create(
            CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, 80, BaseTime, seventeenBuckets));
    }

    [Fact]
    public void Windows_are_an_owned_immutable_copy_of_what_the_caller_supplied()
    {
        var supplied = new List<CodexAccountUsageWindowFact> { new("codex", CodexAccountUsageWindowKind.Primary, 90) };
        var decision = AgentCodexAccountUsageDecision.Create(
            CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, 80, BaseTime, supplied);
        var before = decision.Serialize();

        supplied[0] = new CodexAccountUsageWindowFact("codex", CodexAccountUsageWindowKind.Primary, 1);
        supplied.Add(new CodexAccountUsageWindowFact("x", CodexAccountUsageWindowKind.Primary, 2));
        supplied.Clear();

        Assert.Equal(before, decision.Serialize());
        Assert.Equal(90, Assert.Single(decision.Windows).UsedPercent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("not json")]
    public void FromPersisted_rejects_text_that_is_not_a_decision(string stored)
    {
        Assert.Null(AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, stored));
    }

    [Fact]
    public void FromPersisted_rejects_another_provider_a_null_text_and_oversized_text()
    {
        Assert.Null(AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.ClaudeCode, Canonical));
        Assert.Null(AgentCodexAccountUsageDecision.FromPersisted(null, Canonical));
        Assert.Null(AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, null));
        Assert.Null(AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, Canonical + new string(' ', 9000)));
    }

    [Theory]
    [InlineData("\"version\":1", "\"version\":2")]
    [InlineData("\"codex-account-rate-limits-v1\"", "\"other-source\"")]
    [InlineData("\"reached\"", "\"unavailable\"")]
    [InlineData("\"threshold_reached\"", "\"mystery\"")]
    [InlineData("\"thresholdPercent\":80", "\"thresholdPercent\":\"80\"")]
    [InlineData("\"thresholdPercent\":80", "\"thresholdPercent\":80.5")]
    [InlineData("\"usedPercent\":80", "\"usedPercent\":79")]
    [InlineData("\"usedPercent\":10", "\"usedPercent\":150")]
    [InlineData("\"primary\"", "\"third\"")]
    [InlineData("2026-10-04T12:00:00.0000000Z", "2026-10-04T12:00:00Z")]
    [InlineData("2026-10-04T12:00:00.0000000Z", "2026-10-04T12:00:00.0000000+00:00")]
    [InlineData("2026-10-04T12:00:00.0000000Z", "not a date")]
    public void FromPersisted_rejects_a_text_that_is_not_exactly_the_canonical_form(string find, string replace)
    {
        Assert.Null(AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, Canonical.Replace(find, replace, StringComparison.Ordinal)));
    }

    [Fact]
    public void FromPersisted_rejects_reordered_windows_extra_members_and_duplicates()
    {
        var reordered = Canonical.Replace(
            "{\"bucket\":\"codex\",\"window\":\"primary\",\"usedPercent\":80},{\"bucket\":\"codex\",\"window\":\"secondary\",\"usedPercent\":10}",
            "{\"bucket\":\"codex\",\"window\":\"secondary\",\"usedPercent\":10},{\"bucket\":\"codex\",\"window\":\"primary\",\"usedPercent\":80}",
            StringComparison.Ordinal);
        var extra = Canonical.Replace("\"version\":1", "\"version\":1,\"account\":\"someone\"", StringComparison.Ordinal);
        var duplicate = Canonical.Replace(
            "{\"bucket\":\"codex\",\"window\":\"secondary\",\"usedPercent\":10}",
            "{\"bucket\":\"codex\",\"window\":\"primary\",\"usedPercent\":80}", StringComparison.Ordinal);

        Assert.Null(AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, reordered));
        Assert.Null(AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, extra));
        Assert.Null(AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, duplicate));
    }

    [Fact]
    public void A_reached_decision_completes_the_attempt_atomically_and_failed_with_its_outcome()
    {
        var attempt = ClaimCodex(80);

        attempt.CompleteAgentAccountUsageStop(Reached(80), BaseTime.AddSeconds(5));

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.AccountUsageStopReached, attempt.AgentOutcome);
        Assert.Equal(BaseTime.AddSeconds(5), attempt.CompletedAtUtc);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(Canonical, attempt.AgentAccountUsageDecisionSnapshot);
        Assert.Equal(Canonical, attempt.GetAgentAccountUsageDecision()!.Serialize());
        Assert.Null(attempt.GetAgentModelContextLimitsEvidence());
        Assert.Null(attempt.GetAgentTokenUsageEvidence());
    }

    [Theory]
    [InlineData(CodexAccountUsageDecisionReason.EvidenceUnavailable)]
    [InlineData(CodexAccountUsageDecisionReason.EvidenceExpired)]
    public void An_unavailable_decision_completes_with_the_evidence_unavailable_outcome(CodexAccountUsageDecisionReason reason)
    {
        var attempt = ClaimCodex(80);

        attempt.CompleteAgentAccountUsageStop(Unavailable(reason), BaseTime);

        Assert.Equal(AgentOutcome.AccountUsageEvidenceUnavailable, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(reason, attempt.GetAgentAccountUsageDecision()!.Reason);
    }

    [Fact]
    public void A_malformed_threshold_snapshot_is_resolved_only_by_an_unusable_threshold_decision()
    {
        var attempt = ClaimCodex(null);
        SetStored(attempt, "_agentCodexAccountUsageStopPercent", "t:80");

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgentAccountUsageStop(Reached(80), BaseTime));
        Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgentAccountUsageStop(Unavailable(), BaseTime));
        Assert.Equal(AttemptStatus.Running, attempt.Status);

        attempt.CompleteAgentAccountUsageStop(Unavailable(CodexAccountUsageDecisionReason.ThresholdUnusable, threshold: null), BaseTime);

        Assert.Equal(AgentOutcome.AccountUsageEvidenceUnavailable, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
    }

    [Fact]
    public void An_unusable_threshold_decision_is_refused_for_a_valid_snapshot()
    {
        var attempt = ClaimCodex(80);

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgentAccountUsageStop(
            Unavailable(CodexAccountUsageDecisionReason.ThresholdUnusable, threshold: null), BaseTime));

        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentAccountUsageDecisionSnapshot);
    }

    [Fact]
    public void A_decision_for_another_threshold_is_refused_without_mutation()
    {
        var attempt = ClaimCodex(80);

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgentAccountUsageStop(Reached(50), BaseTime));

        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.AgentAccountUsageDecisionSnapshot);
    }

    [Fact]
    public void An_attempt_that_never_claimed_a_stop_cannot_record_one()
    {
        var attempt = ClaimCodex(null);

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgentAccountUsageStop(Reached(80), BaseTime));

        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public void A_dispatched_attempt_a_second_decision_and_a_completed_attempt_are_refused()
    {
        var dispatched = ClaimCodex(80);
        dispatched.MarkAgentDispatched(BaseTime);
        Assert.Throws<InvalidOperationException>(() => dispatched.CompleteAgentAccountUsageStop(Reached(80), BaseTime));
        Assert.Null(dispatched.AgentAccountUsageDecisionSnapshot);

        var stopped = ClaimCodex(80);
        stopped.CompleteAgentAccountUsageStop(Reached(80), BaseTime);
        Assert.Throws<InvalidOperationException>(() => stopped.CompleteAgentAccountUsageStop(Reached(80), BaseTime.AddSeconds(1)));
        Assert.Equal(BaseTime, stopped.CompletedAtUtc);
    }

    [Fact]
    public void Another_provider_and_a_non_agent_attempt_cannot_record_a_decision()
    {
        var process = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);
        Assert.Throws<InvalidOperationException>(() => process.CompleteAgentAccountUsageStop(Reached(80), BaseTime));
    }

    [Fact]
    public void The_two_outcomes_are_pre_invocation_and_cannot_carry_process_evidence_or_a_second_snapshot()
    {
        Assert.True(AgentProcessEvidencePolicy.IsPreInvocationOutcome(AgentOutcome.AccountUsageStopReached));
        Assert.True(AgentProcessEvidencePolicy.IsPreInvocationOutcome(AgentOutcome.AccountUsageEvidenceUnavailable));
    }

    [Fact]
    public void A_tampered_stored_decision_reads_as_unknown_beside_a_valid_outcome_and_never_throws()
    {
        var attempt = ClaimCodex(80);
        attempt.CompleteAgentAccountUsageStop(Reached(80), BaseTime);
        SetStoredProperty(attempt, nameof(Attempt.AgentAccountUsageDecisionSnapshot), Canonical.Replace("\"usedPercent\":80", "\"usedPercent\":79", StringComparison.Ordinal));

        Assert.Null(attempt.GetAgentAccountUsageDecision());
        Assert.Equal(AgentOutcome.AccountUsageStopReached, attempt.AgentOutcome);
    }

    [Fact]
    public void A_well_formed_snapshot_is_never_trusted_for_a_running_attempt_or_a_different_outcome()
    {
        var running = ClaimCodex(80);
        SetStoredProperty(running, nameof(Attempt.AgentAccountUsageDecisionSnapshot), Canonical);
        Assert.Null(running.GetAgentAccountUsageDecision());

        var failedElsewhere = ClaimCodex(80);
        failedElsewhere.CompleteAgent(AgentOutcome.WorkspaceNoLongerEligible, null, BaseTime);
        SetStoredProperty(failedElsewhere, nameof(Attempt.AgentAccountUsageDecisionSnapshot), Canonical);
        Assert.Null(failedElsewhere.GetAgentAccountUsageDecision());
    }

    [Fact]
    public void A_canonical_decision_for_another_threshold_than_the_attempts_snapshot_reads_as_unknown()
    {
        var attempt = ClaimCodex(80);
        attempt.CompleteAgentAccountUsageStop(Reached(80), BaseTime);
        SetStoredProperty(attempt, nameof(Attempt.AgentAccountUsageDecisionSnapshot), Canonical.Replace("\"thresholdPercent\":80", "\"thresholdPercent\":50", StringComparison.Ordinal));
        Assert.NotNull(AgentCodexAccountUsageDecision.FromPersisted(AgentProvider.Codex, attempt.AgentAccountUsageDecisionSnapshot));

        Assert.Null(attempt.GetAgentAccountUsageDecision());
        Assert.Equal(AgentOutcome.AccountUsageStopReached, attempt.AgentOutcome);
    }

    [Fact]
    public void A_decision_is_unknown_when_the_snapshot_is_absent_changed_or_malformed_and_the_decision_ordinary()
    {
        var absent = ClaimCodex(80);
        absent.CompleteAgentAccountUsageStop(Reached(80), BaseTime);
        SetStored(absent, "_agentCodexAccountUsageStopPercent", null);
        Assert.Null(absent.GetAgentAccountUsageDecision());

        var changed = ClaimCodex(80);
        changed.CompleteAgentAccountUsageStop(Reached(80), BaseTime);
        SetStored(changed, "_agentCodexAccountUsageStopPercent", CodexAccountUsageStop.Format(90));
        Assert.Null(changed.GetAgentAccountUsageDecision());

        var malformed = ClaimCodex(80);
        malformed.CompleteAgentAccountUsageStop(Reached(80), BaseTime);
        SetStored(malformed, "_agentCodexAccountUsageStopPercent", "t:80");
        Assert.Null(malformed.GetAgentAccountUsageDecision());
    }

    [Fact]
    public void An_unusable_threshold_decision_is_known_only_beside_a_really_malformed_snapshot()
    {
        var malformed = ClaimCodex(null);
        SetStored(malformed, "_agentCodexAccountUsageStopPercent", "t:80");
        malformed.CompleteAgentAccountUsageStop(Unavailable(CodexAccountUsageDecisionReason.ThresholdUnusable, threshold: null), BaseTime);
        Assert.Equal(CodexAccountUsageDecisionReason.ThresholdUnusable, malformed.GetAgentAccountUsageDecision()!.Reason);

        SetStored(malformed, "_agentCodexAccountUsageStopPercent", CodexAccountUsageStop.Format(80));
        Assert.Null(malformed.GetAgentAccountUsageDecision());
        SetStored(malformed, "_agentCodexAccountUsageStopPercent", null);
        Assert.Null(malformed.GetAgentAccountUsageDecision());
    }

    [Fact]
    public void An_ordinary_decision_beside_an_equal_valid_snapshot_stays_known()
    {
        var attempt = ClaimCodex(80);
        attempt.CompleteAgentAccountUsageStop(Reached(80), BaseTime);
        Assert.Equal(80, attempt.GetAgentAccountUsageDecision()!.ThresholdPercent);

        var unavailable = ClaimCodex(80);
        unavailable.CompleteAgentAccountUsageStop(Unavailable(CodexAccountUsageDecisionReason.EvidenceExpired, 80, BaseTime), BaseTime);
        Assert.Equal(CodexAccountUsageDecisionReason.EvidenceExpired, unavailable.GetAgentAccountUsageDecision()!.Reason);
    }

    private static void SetStoredProperty(object target, string property, string? value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(target, value);
}
