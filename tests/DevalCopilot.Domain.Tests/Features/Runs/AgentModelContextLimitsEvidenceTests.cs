using System.Text;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentModelContextLimitsEvidenceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private const string Fingerprint = "fingerprint-1";
    private const string Source = "claude-cli-model-usage-v1";

    private const string CanonicalTwo =
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"claude-a\",\"contextWindowTokens\":200000,"
        + "\"maxOutputTokens\":32000},{\"modelId\":\"claude-b\",\"contextWindowTokens\":1000000,\"maxOutputTokens\":64000}]}";

    private static readonly AgentModelContextLimit A = new("claude-a", 200000, 32000);
    private static readonly AgentModelContextLimit B = new("claude-b", 1000000, 64000);

    public static TheoryData<AgentOutcome> AllOutcomes()
    {
        var data = new TheoryData<AgentOutcome>();
        foreach (var outcome in Enum.GetValues<AgentOutcome>())
        {
            data.Add(outcome);
        }

        return data;
    }

    [Fact]
    public void Create_orders_the_entries_ordinally_and_serializes_the_one_canonical_text()
    {
        var evidence = AgentModelContextLimitsEvidence.Create(Source, [B, A]);

        Assert.Equal(Source, evidence.Source);
        Assert.Equal([A, B], evidence.Models.AsEnumerable());
        Assert.Equal(CanonicalTwo, evidence.Serialize());
    }

    [Fact]
    public void Ordering_is_ordinal_not_culture_or_case_insensitive()
    {
        var evidence = AgentModelContextLimitsEvidence.Create(
            Source, [new("b", 2, 2), new("a", 2, 2), new("B", 2, 2), new("A", 2, 2), new("a-1", 2, 2), new("a.1", 2, 2), new("a_1", 2, 2)]);

        Assert.Equal(["A", "B", "a", "a-1", "a.1", "a_1", "b"], evidence.Models.Select(model => model.ModelId));
    }

    [Fact]
    public void FromPersisted_reads_back_exactly_what_Serialize_wrote()
    {
        var written = AgentModelContextLimitsEvidence.Create(Source, [B, A]);

        var read = AgentModelContextLimitsEvidence.FromPersisted(AgentProvider.ClaudeCode, written.Serialize());

        Assert.NotNull(read);
        Assert.Equal(written.Source, read.Source);
        Assert.Equal(written.Models, read.Models.AsEnumerable());
        Assert.Equal(CanonicalTwo, read.Serialize());
    }

    [Fact]
    public void The_largest_valid_evidence_stays_inside_the_four_kibibyte_bound_with_room_to_spare()
    {
        var models = Enumerable.Range(0, AgentModelContextLimitsEvidence.MaxModels)
            .Select(index => new AgentModelContextLimit($"{index:00}{new string('z', 126)}", int.MaxValue, int.MaxValue))
            .ToList();

        var evidence = AgentModelContextLimitsEvidence.Create(Source, models);

        var bytes = Encoding.UTF8.GetByteCount(evidence.Serialize());
        Assert.InRange(bytes, 3000, AgentModelContextLimitsEvidence.MaxSerializedBytes);
        Assert.Equal(evidence.Models, AgentModelContextLimitsEvidence.FromPersisted(AgentProvider.ClaudeCode, evidence.Serialize())!.Models.AsEnumerable());
    }

    [Fact]
    public void The_bounds_are_exact()
    {
        Assert.Null(AgentModelContextLimitsEvidence.Validate(Source, [new(new string('a', 128), 1, 1)]));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.InvalidModelId,
            AgentModelContextLimitsEvidence.Validate(Source, [new(new string('a', 129), 1, 1)]));
        Assert.Null(AgentModelContextLimitsEvidence.Validate(Source, [new("a", int.MaxValue, int.MaxValue)]));
        Assert.Null(AgentModelContextLimitsEvidence.Validate(Source, [new("a", 7, 7)]));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.MaxOutputExceedsContextWindow,
            AgentModelContextLimitsEvidence.Validate(Source, [new("a", 7, 8)]));
        Assert.Null(AgentModelContextLimitsEvidence.Validate(Source, Enumerable.Range(0, 16).Select(i => new AgentModelContextLimit($"m{i}", 1, 1)).ToList()));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.TooManyModels,
            AgentModelContextLimitsEvidence.Validate(Source, Enumerable.Range(0, 17).Select(i => new AgentModelContextLimit($"m{i}", 1, 1)).ToList()));
        Assert.Null(AgentModelContextLimitsEvidence.Validate(new string('s', 128), [new("a", 1, 1)]));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.InvalidSource,
            AgentModelContextLimitsEvidence.Validate(new string('s', 129), [new("a", 1, 1)]));
    }

    [Theory]
    [InlineData("", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("-a", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData(".a", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("_a", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("a b", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("a/b", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("a[1m]", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("modèl", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("模型", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("a\u0000b", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("a\nb", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("a\uD800", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    [InlineData("Ａ", AgentModelContextLimitsEvidenceViolation.InvalidModelId)]
    public void Identifiers_outside_the_closed_ascii_shape_are_refused(string modelId, AgentModelContextLimitsEvidenceViolation expected)
    {
        Assert.Equal(expected, AgentModelContextLimitsEvidence.Validate(Source, [new(modelId, 1, 1)]));
        Assert.Throws<ArgumentException>(() => AgentModelContextLimitsEvidence.Create(Source, [new(modelId, 1, 1)]));
    }

    [Theory]
    [InlineData("A0._-z")]
    [InlineData("claude-opus-4-5-20251101")]
    [InlineData("0")]
    public void Identifiers_inside_the_closed_ascii_shape_are_kept_exactly_as_provided(string modelId)
    {
        Assert.Equal(modelId, Assert.Single(AgentModelContextLimitsEvidence.Create(Source, [new(modelId, 1, 1)]).Models).ModelId);
    }

    [Fact]
    public void Every_other_violation_is_reported_by_its_own_rule()
    {
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.NoModels, AgentModelContextLimitsEvidence.Validate(Source, []));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.NoModels, AgentModelContextLimitsEvidence.Validate(Source, null));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.InvalidSource, AgentModelContextLimitsEvidence.Validate(null, [A]));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.InvalidSource, AgentModelContextLimitsEvidence.Validate(string.Empty, [A]));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.InvalidSource, AgentModelContextLimitsEvidence.Validate("has space", [A]));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.InvalidSource, AgentModelContextLimitsEvidence.Validate("café", [A]));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.InvalidSource, AgentModelContextLimitsEvidence.Validate("a\uD800", [A]));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.DuplicateModelId, AgentModelContextLimitsEvidence.Validate(Source, [A, A]));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.DuplicateModelId,
            AgentModelContextLimitsEvidence.Validate(Source, [A, new AgentModelContextLimit("claude-a", 1, 1)]));
        Assert.Null(AgentModelContextLimitsEvidence.Validate(Source, [new("a", 1, 1), new("A", 1, 1)]));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.InvalidContextWindowTokens, AgentModelContextLimitsEvidence.Validate(Source, [new("a", 0, 1)]));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.InvalidContextWindowTokens, AgentModelContextLimitsEvidence.Validate(Source, [new("a", -1, 1)]));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.InvalidMaxOutputTokens, AgentModelContextLimitsEvidence.Validate(Source, [new("a", 1, 0)]));
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.InvalidMaxOutputTokens, AgentModelContextLimitsEvidence.Validate(Source, [new("a", 1, -1)]));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.MaxOutputExceedsContextWindow, AgentModelContextLimitsEvidence.Validate(Source, [new("a", 5, 6)]));
    }

    public static TheoryData<string?> NonCanonical => new()
    {
        null, string.Empty, " ", "x", "[]", "{}", "{\"version\":1}", CanonicalTwo + " ", " " + CanonicalTwo, CanonicalTwo.Replace(":1,", ": 1,"),
        CanonicalTwo.Replace("\"version\":1", "\"version\":2"), CanonicalTwo.Replace("\"version\":1", "\"version\":1.0"),
        CanonicalTwo.Replace("\"version\":1", "\"version\":true"), CanonicalTwo.Replace(Source, "claude-cli-model-usage-v2"),
        CanonicalTwo.Replace("200000", "200000.0"), CanonicalTwo.Replace("200000", "\"200000\""),
        CanonicalTwo.Replace("200000", "2e5"), CanonicalTwo.Replace("32000", "200001"), CanonicalTwo.Replace("32000", "0"),
        CanonicalTwo.Replace("{\"version\":1,", "{\"version\":1,\"extra\":1,"), CanonicalTwo.Replace("\"models\":[", "\"models\":[null,"),
        CanonicalTwo.Replace("\"maxOutputTokens\":32000", "\"maxOutputTokens\":32000,\"maxOutputTokens\":32000"),
        CanonicalTwo.Replace("claude-a", "claude-b"), CanonicalTwo.Replace("claude-a", "\\u0063laude-a"),
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"claude-b\",\"contextWindowTokens\":1,\"maxOutputTokens\":1},"
        + "{\"modelId\":\"claude-a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}",
        new string('[', 2000), "{\"version\":1,\"source\":\"\uD800\",\"models\":[]}", "\uD800", new string('a', 5000),
    };

    [Theory]
    [MemberData(nameof(NonCanonical))]
    public void FromPersisted_reads_nothing_that_is_not_exactly_canonical_valid_evidence_and_never_throws(string? stored)
    {
        Assert.Null(AgentModelContextLimitsEvidence.FromPersisted(AgentProvider.ClaudeCode, stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AgentProvider.Codex)]
    public void FromPersisted_requires_the_exact_provider_and_source_pair(AgentProvider? provider)
    {
        Assert.Null(AgentModelContextLimitsEvidence.FromPersisted(provider, CanonicalTwo));
        Assert.NotNull(AgentModelContextLimitsEvidence.FromPersisted(AgentProvider.ClaudeCode, CanonicalTwo));
    }

    [Theory]
    [MemberData(nameof(AllOutcomes))]
    public void Policy_rejects_evidence_exactly_for_the_shared_pre_invocation_outcome_set(AgentOutcome outcome)
    {
        var evidence = AgentModelContextLimitsEvidence.Create(Source, [A]);
        var expected = AgentProcessEvidencePolicy.IsPreInvocationOutcome(outcome)
            ? AgentModelContextLimitsEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence
            : (AgentModelContextLimitsEvidenceViolation?)null;

        Assert.Equal(expected, AgentModelContextLimitsEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, true, evidence));
        Assert.Null(AgentModelContextLimitsEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, true, null));
        Assert.Null(AgentModelContextLimitsEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, false, null));
    }

    [Fact]
    public void Policy_requires_dispatch_and_the_exact_provider_and_source()
    {
        var evidence = AgentModelContextLimitsEvidence.Create(Source, [A]);
        var otherSource = AgentModelContextLimitsEvidence.Create("claude-cli-model-usage-v2", [A]);

        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.NotDispatched,
            AgentModelContextLimitsEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, false, evidence));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.UnsupportedProviderSource,
            AgentModelContextLimitsEvidencePolicy.Evaluate(AgentProvider.Codex, AgentOutcome.Proposed, true, evidence));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.UnsupportedProviderSource,
            AgentModelContextLimitsEvidencePolicy.Evaluate(null, AgentOutcome.Proposed, true, evidence));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.UnsupportedProviderSource,
            AgentModelContextLimitsEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, AgentOutcome.Proposed, true, otherSource));
    }

    // Unlike process evidence, limits never gate on a clean exit and are independent of the outcome: every post-invocation
    // outcome, success or not, may carry them or go without.
    [Theory]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.SourceChanged)]
    [InlineData(AgentOutcome.CheckpointEvidenceUnavailable)]
    public void Policy_never_requires_limits_and_accepts_them_for_any_post_invocation_outcome(AgentOutcome outcome)
    {
        var evidence = AgentModelContextLimitsEvidence.Create(Source, [A]);

        Assert.Null(AgentModelContextLimitsEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, true, null));
        Assert.Null(AgentModelContextLimitsEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, true, evidence));
    }

    [Fact]
    public void CompleteAgent_records_the_limits_atomically_with_the_outcome_and_independently_of_the_usage()
    {
        var attempt = ClaimDispatchedCriticalReview();
        var limits = AgentModelContextLimitsEvidence.Create(Source, [B, A]);

        attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, BaseTime.AddSeconds(2), TestProcessEvidence.CleanExit, null, limits);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Accepted, attempt.AgentOutcome);
        Assert.Equal(CanonicalTwo, attempt.AgentModelContextLimitsSnapshot);
        Assert.Equal([A, B], attempt.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());
        Assert.Null(attempt.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public void Usage_and_limits_are_recorded_together_and_each_is_read_back_on_its_own()
    {
        var attempt = ClaimDispatchedCriticalReview();

        attempt.CompleteAgent(
            AgentOutcome.InvalidStructuredOutput, Fingerprint, BaseTime, TestProcessEvidence.CleanExit, TestTokenUsage.Reported,
            AgentModelContextLimitsEvidence.Create(Source, [A]));

        Assert.Equal(AgentOutcome.InvalidStructuredOutput, attempt.AgentOutcome);
        Assert.Equal(TestTokenUsage.Reported, attempt.GetAgentTokenUsageEvidence());
        Assert.Equal([A], attempt.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());
    }

    [Fact]
    public void CompleteAgent_keeps_the_limits_when_source_drift_overrides_the_semantic_outcome()
    {
        var attempt = ClaimDispatchedCriticalReview();

        attempt.CompleteAgent(
            AgentOutcome.Accepted, "fingerprint-drifted", BaseTime, TestProcessEvidence.CleanExit, null,
            AgentModelContextLimitsEvidence.Create(Source, [A]));

        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Equal([A], attempt.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());
    }

    [Fact]
    public void CompleteImplementation_records_the_limits_for_a_success_and_for_a_failure()
    {
        var implemented = ClaimDispatchedImplementation();
        implemented.CompleteImplementation(
            AgentOutcome.Implemented, Guid.NewGuid(), BaseTime, TestProcessEvidence.CleanExit, null,
            AgentModelContextLimitsEvidence.Create(Source, [A]));
        Assert.Equal(AttemptStatus.Completed, implemented.Status);
        Assert.Equal([A], implemented.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());

        var failed = ClaimDispatchedImplementation();
        failed.CompleteImplementation(
            AgentOutcome.ProviderInvocationFailed, null, BaseTime,
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(1)), null,
            AgentModelContextLimitsEvidence.Create(Source, [B]));
        Assert.Equal(AttemptStatus.Failed, failed.Status);
        Assert.Equal([B], failed.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());
    }

    [Fact]
    public void CompleteReviewCorrection_records_the_limits_for_a_success_and_for_a_failure()
    {
        var corrected = ClaimDispatchedCorrection();
        corrected.CompleteReviewCorrection(
            AgentOutcome.CorrectionApplied, Guid.NewGuid(), BaseTime, TestProcessEvidence.CleanExit, null,
            AgentModelContextLimitsEvidence.Create(Source, [A, B]));
        Assert.Equal(AttemptStatus.Completed, corrected.Status);
        Assert.Equal([A, B], corrected.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());

        var failed = ClaimDispatchedCorrection();
        failed.CompleteReviewCorrection(
            AgentOutcome.InvalidStructuredOutput, null, BaseTime, TestProcessEvidence.CleanExit, null,
            AgentModelContextLimitsEvidence.Create(Source, [A]));
        Assert.Equal(AttemptStatus.Failed, failed.Status);
        Assert.Equal([A], failed.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());
    }

    [Fact]
    public void An_outcome_recorded_without_limits_never_fabricates_them()
    {
        var attempt = ClaimDispatchedCriticalReview();

        attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, BaseTime, TestProcessEvidence.CleanExit, TestTokenUsage.Reported);

        Assert.Null(attempt.AgentModelContextLimitsSnapshot);
        Assert.Null(attempt.GetAgentModelContextLimitsEvidence());
    }

    [Fact]
    public void A_codex_attempt_rejects_limits_before_any_transition_mutation()
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, BaseTime, 1);
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgent(
            AgentOutcome.ProviderInvocationFailed, null, BaseTime, TestProcessEvidence.CleanExit, null,
            AgentModelContextLimitsEvidence.Create(Source, [A])));

        AssertUntouched(attempt);
    }

    [Fact]
    public void An_undispatched_attempt_or_a_pre_invocation_outcome_rejects_limits_without_mutation()
    {
        var undispatched = ClaimCriticalReview();
        Assert.Throws<InvalidOperationException>(() => undispatched.CompleteAgent(
            AgentOutcome.SourceChanged, null, BaseTime, null, null, AgentModelContextLimitsEvidence.Create(Source, [A])));
        AssertUntouched(undispatched);

        var raced = ClaimDispatchedImplementation();
        Assert.Throws<InvalidOperationException>(() => raced.CompleteImplementation(
            AgentOutcome.InputAlreadyImplemented, null, BaseTime, null, null, AgentModelContextLimitsEvidence.Create(Source, [A])));
        AssertUntouched(raced);

        var correctionRaced = ClaimDispatchedCorrection();
        Assert.Throws<InvalidOperationException>(() => correctionRaced.CompleteReviewCorrection(
            AgentOutcome.InputAlreadyCorrected, null, BaseTime, null, null, AgentModelContextLimitsEvidence.Create(Source, [A])));
        AssertUntouched(correctionRaced);
    }

    [Fact]
    public void A_failed_completion_leaves_the_attempt_untouched_and_never_half_records_the_limits()
    {
        var attempt = ClaimDispatchedImplementation();

        // A success outcome without the resulting checkpoint is refused by an unrelated Domain rule.
        Assert.Throws<ArgumentException>(() => attempt.CompleteImplementation(
            AgentOutcome.Implemented, null, BaseTime, TestProcessEvidence.CleanExit, null,
            AgentModelContextLimitsEvidence.Create(Source, [A])));

        AssertUntouched(attempt);
    }

    [Fact]
    public void Interrupt_never_invents_limits_for_a_dispatched_attempt()
    {
        var attempt = ClaimDispatchedImplementation();

        attempt.Interrupt(BaseTime.AddMinutes(1));

        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Null(attempt.AgentModelContextLimitsSnapshot);
        Assert.Null(attempt.GetAgentModelContextLimitsEvidence());
    }

    [Fact]
    public void A_still_running_attempt_and_a_non_agent_attempt_expose_no_limits()
    {
        Assert.Null(ClaimDispatchedCriticalReview().GetAgentModelContextLimitsEvidence());
        Assert.Null(Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime).GetAgentModelContextLimitsEvidence());
    }

    // R1: a validated value owns an immutable snapshot. The caller's collection and the public collection surface can never
    // change what it reports, serializes or records.
    private static void TryReplaceThroughTheCollectionInterfaces(IReadOnlyList<AgentModelContextLimit> view, AgentModelContextLimit replacement)
    {
        if (view is IList<AgentModelContextLimit> list)
        {
            foreach (var attempt in new Action[] { () => list[0] = replacement, () => list.Add(replacement), () => list.Clear(), () => list.RemoveAt(0) })
            {
                try
                {
                    attempt();
                }
                catch (NotSupportedException)
                {
                }
            }
        }

        if (view is System.Collections.IList legacy)
        {
            foreach (var attempt in new Action[] { () => legacy[0] = replacement, () => legacy.Clear() })
            {
                try
                {
                    attempt();
                }
                catch (Exception exception) when (exception is NotSupportedException or ArgumentException or InvalidCastException)
                {
                }
            }
        }
    }

    [Fact]
    public void Create_owns_its_entries_so_changing_the_callers_collection_afterwards_changes_nothing()
    {
        var source = new List<AgentModelContextLimit> { B, A };
        var evidence = AgentModelContextLimitsEvidence.Create(Source, source);

        source[0] = new AgentModelContextLimit("tampered", 777, 7);
        source.Add(new AgentModelContextLimit("extra", 1, 1));
        source.Clear();

        Assert.Equal([A, B], evidence.Models.AsEnumerable());
        Assert.Equal(CanonicalTwo, evidence.Serialize());
    }

    [Fact]
    public void Create_never_shares_an_array_the_caller_still_holds()
    {
        var source = new[] { B, A };
        var evidence = AgentModelContextLimitsEvidence.Create(Source, source);

        source[0] = new AgentModelContextLimit("tampered", 777, 7);
        source[1] = new AgentModelContextLimit("-1", -1, 0);

        Assert.Equal([A, B], evidence.Models.AsEnumerable());
        Assert.Equal(CanonicalTwo, evidence.Serialize());
    }

    [Fact]
    public void Create_exposes_no_collection_a_caller_can_write_through()
    {
        var evidence = AgentModelContextLimitsEvidence.Create(Source, [B, A]);

        TryReplaceThroughTheCollectionInterfaces(evidence.Models, new AgentModelContextLimit("tampered", 777, 7));

        Assert.Equal([A, B], evidence.Models.AsEnumerable());
        Assert.Equal(CanonicalTwo, evidence.Serialize());
    }

    [Fact]
    public void FromPersisted_exposes_no_collection_a_caller_can_write_through()
    {
        var evidence = AgentModelContextLimitsEvidence.FromPersisted(AgentProvider.ClaudeCode, CanonicalTwo)!;

        TryReplaceThroughTheCollectionInterfaces(evidence.Models, new AgentModelContextLimit("bad", -1, 0));

        Assert.Equal([A, B], evidence.Models.AsEnumerable());
        Assert.Equal(CanonicalTwo, evidence.Serialize());
        Assert.Equal(CanonicalTwo, AgentModelContextLimitsEvidence.FromPersisted(AgentProvider.ClaudeCode, evidence.Serialize())!.Serialize());
    }

    [Fact]
    public void A_completion_records_exactly_the_validated_values_whatever_is_done_to_the_evidence_or_its_source_afterwards()
    {
        var source = new List<AgentModelContextLimit> { B, A };
        var evidence = AgentModelContextLimitsEvidence.Create(Source, source);
        source[0] = new AgentModelContextLimit("tampered", 777, 7);
        TryReplaceThroughTheCollectionInterfaces(evidence.Models, new AgentModelContextLimit("bad", -1, 0));
        var attempt = ClaimDispatchedCriticalReview();

        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, BaseTime, TestProcessEvidence.CleanExit, null, evidence);

        Assert.Equal(CanonicalTwo, attempt.AgentModelContextLimitsSnapshot);
        Assert.Equal([A, B], attempt.GetAgentModelContextLimitsEvidence()!.Models.AsEnumerable());
    }

    [Fact]
    public void A_restored_value_that_was_tampered_with_through_its_collection_can_never_record_an_invalid_snapshot()
    {
        var restored = AgentModelContextLimitsEvidence.FromPersisted(AgentProvider.ClaudeCode, CanonicalTwo)!;
        TryReplaceThroughTheCollectionInterfaces(restored.Models, new AgentModelContextLimit("bad id", -1, 0));
        var attempt = ClaimDispatchedImplementation();

        attempt.CompleteImplementation(AgentOutcome.ProviderInvocationFailed, null, BaseTime, TestProcessEvidence.CleanExit, null, restored);

        Assert.Equal(CanonicalTwo, attempt.AgentModelContextLimitsSnapshot);
        Assert.NotNull(attempt.GetAgentModelContextLimitsEvidence());
    }

    // R2: the count is judged before any entry is read or copied, so an excessive or lying collection is refused without a
    // traversal or an unbounded copy.
    private sealed class CountingList(int count) : IReadOnlyList<AgentModelContextLimit>
    {
        public int Reads { get; private set; }

        public int Count => count;

        public AgentModelContextLimit this[int index]
        {
            get
            {
                Reads++;
                return new AgentModelContextLimit($"m{index}", 1, 1);
            }
        }

        public IEnumerator<AgentModelContextLimit> GetEnumerator()
        {
            for (var index = 0; index < count; index++)
            {
                Reads++;
                yield return new AgentModelContextLimit($"m{index}", 1, 1);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void An_excessive_collection_is_refused_before_any_entry_is_read()
    {
        var excessive = new CountingList(1_000_000);

        Assert.Equal(AgentModelContextLimitsEvidenceViolation.TooManyModels, AgentModelContextLimitsEvidence.Validate(Source, excessive));
        Assert.Throws<ArgumentException>(() => AgentModelContextLimitsEvidence.Create(Source, excessive));
        Assert.Equal(0, excessive.Reads);
    }

    [Fact]
    public void A_null_collection_and_a_null_entry_are_refused_without_throwing_from_validation()
    {
        Assert.Equal(AgentModelContextLimitsEvidenceViolation.NoModels, AgentModelContextLimitsEvidence.Validate(Source, null));
        Assert.Equal(
            AgentModelContextLimitsEvidenceViolation.InvalidModelId,
            AgentModelContextLimitsEvidence.Validate(Source, [A, null!]));
        Assert.Throws<ArgumentException>(() => AgentModelContextLimitsEvidence.Create(Source, [A, null!]));
    }

    private static void AssertUntouched(Attempt attempt)
    {
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.AgentProcessOutcome);
        Assert.Null(attempt.AgentModelContextLimitsSnapshot);
        Assert.Null(attempt.GetAgentModelContextLimitsEvidence());
    }

    private static Attempt ClaimCriticalReview() => Attempt.ClaimAgentCriticalReview(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, BaseTime, 1);

    private static Attempt ClaimDispatchedCriticalReview()
    {
        var attempt = ClaimCriticalReview();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        return attempt;
    }

    private static Attempt ClaimDispatchedImplementation()
    {
        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 1024, 2048, BaseTime, 1);
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        return attempt;
    }

    private static Attempt ClaimDispatchedCorrection()
    {
        var attempt = Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 1024, 2048, BaseTime, 1);
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        return attempt;
    }
}
