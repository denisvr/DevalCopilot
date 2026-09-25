using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentTokenUsageEvidenceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private const string Fingerprint = "fingerprint-1";

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
    public void Create_accepts_a_complete_shape_with_cache_breakdown()
    {
        var evidence = AgentTokenUsageEvidence.Create(1200, 345, 67, 890, "claude-cli-usage-v1");

        Assert.Equal(1200, evidence.InputTokens);
        Assert.Equal(345, evidence.OutputTokens);
        Assert.Equal(67, evidence.CacheCreationInputTokens);
        Assert.Equal(890, evidence.CacheReadInputTokens);
        Assert.Equal("claude-cli-usage-v1", evidence.SchemaVersion);
    }

    [Fact]
    public void Create_accepts_zero_counts_and_an_absent_cache_breakdown()
    {
        var evidence = AgentTokenUsageEvidence.Create(0, 0, null, null, "future-provider-usage-v1");

        Assert.Equal(0, evidence.InputTokens);
        Assert.Equal(0, evidence.OutputTokens);
        Assert.Null(evidence.CacheCreationInputTokens);
        Assert.Null(evidence.CacheReadInputTokens);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, "v1", AgentTokenUsageEvidenceViolation.NegativeInputTokens)]
    [InlineData(0, -1, 0, 0, "v1", AgentTokenUsageEvidenceViolation.NegativeOutputTokens)]
    [InlineData(0, 0, -1, 0, "v1", AgentTokenUsageEvidenceViolation.NegativeCacheCreationInputTokens)]
    [InlineData(0, 0, 0, -1, "v1", AgentTokenUsageEvidenceViolation.NegativeCacheReadInputTokens)]
    [InlineData(0, 0, 0, 0, "", AgentTokenUsageEvidenceViolation.InvalidSchemaVersion)]
    [InlineData(0, 0, 0, 0, "   ", AgentTokenUsageEvidenceViolation.InvalidSchemaVersion)]
    [InlineData(0, 0, 0, 0, null, AgentTokenUsageEvidenceViolation.InvalidSchemaVersion)]
    [InlineData(2400, 120, 5, null, "codex-cli-usage-v1", AgentTokenUsageEvidenceViolation.CodexUsageCannotIncludeACacheBreakdown)]
    [InlineData(2400, 120, null, 5, "codex-cli-usage-v1", AgentTokenUsageEvidenceViolation.CodexUsageCannotIncludeACacheBreakdown)]
    [InlineData(2400, 120, 5, 5, "codex-cli-usage-v1", AgentTokenUsageEvidenceViolation.CodexUsageCannotIncludeACacheBreakdown)]
    public void Validate_and_Create_reject_every_invalid_shape(
        int input, int output, int? cacheCreation, int? cacheRead, string? schemaVersion, AgentTokenUsageEvidenceViolation expected)
    {
        Assert.Equal(expected, AgentTokenUsageEvidence.Validate(input, output, cacheCreation, cacheRead, schemaVersion));
        Assert.Throws<ArgumentException>(() => AgentTokenUsageEvidence.Create(input, output, cacheCreation, cacheRead, schemaVersion!));
    }

    [Fact]
    public void Validate_rejects_an_oversized_schema_version_and_accepts_the_maximum_length()
    {
        var maximum = new string('v', AgentTokenUsageEvidence.MaxSchemaVersionLength);
        var oversized = new string('v', AgentTokenUsageEvidence.MaxSchemaVersionLength + 1);

        Assert.Null(AgentTokenUsageEvidence.Validate(1, 1, null, null, maximum));
        Assert.Equal(AgentTokenUsageEvidenceViolation.InvalidSchemaVersion, AgentTokenUsageEvidence.Validate(1, 1, null, null, oversized));
    }

    [Theory]
    [InlineData(null, 5, "v1")]
    [InlineData(5, null, "v1")]
    [InlineData(5, 5, null)]
    [InlineData(-5, 5, "v1")]
    public void FromPersisted_projects_a_missing_or_invalid_row_as_unknown(int? input, int? output, string? schemaVersion)
    {
        Assert.Null(AgentTokenUsageEvidence.FromPersisted(AgentProvider.ClaudeCode, input, output, 1, 1, schemaVersion));
    }

    [Fact]
    public void FromPersisted_projects_an_invalid_cache_member_as_unknown_rather_than_partially_trusted()
    {
        Assert.Null(AgentTokenUsageEvidence.FromPersisted(AgentProvider.ClaudeCode, 10, 20, -3, 4, "v1"));
        Assert.Equal(
            AgentTokenUsageEvidence.Create(10, 20, 3, 4, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion),
            AgentTokenUsageEvidence.FromPersisted(
                AgentProvider.ClaudeCode, 10, 20, 3, 4, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion));
    }

    [Theory]
    [InlineData(AgentProvider.Codex, "claude-cli-usage-v1")]
    [InlineData(AgentProvider.ClaudeCode, "future-provider-usage-v1")]
    [InlineData((AgentProvider)999, "claude-cli-usage-v1")]
    public void FromPersisted_projects_an_unproven_provider_schema_pair_as_unknown(AgentProvider provider, string schemaVersion)
    {
        Assert.Null(AgentTokenUsageEvidence.FromPersisted(provider, 10, 20, 1, 2, schemaVersion));
        Assert.Equal(
            AgentTokenUsageEvidenceViolation.UnsupportedProviderSchema,
            AgentTokenUsageEvidencePolicy.Evaluate(
                provider, AgentOutcome.ProviderInvocationFailed, true,
                AgentTokenUsageEvidence.Create(10, 20, 1, 2, schemaVersion)));
    }

    [Fact]
    public void FromPersisted_projects_missing_provider_provenance_as_unknown()
    {
        Assert.Null(AgentTokenUsageEvidence.FromPersisted(
            null, 10, 20, null, null, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion));
    }

    [Fact]
    public void FromPersisted_accepts_the_proven_codex_provider_schema_pair_without_a_cache_breakdown()
    {
        var evidence = AgentTokenUsageEvidence.FromPersisted(
            AgentProvider.Codex, 2400, 120, null, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion);

        Assert.Equal(TestTokenUsage.CodexReported, evidence);
        Assert.Null(evidence!.CacheCreationInputTokens);
        Assert.Null(evidence.CacheReadInputTokens);
    }

    // A row that could only exist through manual tampering or a future bug, never through this
    // application's own recording path (which now rejects the shape before it can ever be
    // persisted) — FromPersisted is the single choke point Attempt.GetAgentTokenUsageEvidence and
    // the run-cockpit aggregate both use, so this one assertion also proves the cockpit sum never
    // trusts such a row as known usage.
    [Theory]
    [InlineData(5, null)]
    [InlineData(null, 5)]
    [InlineData(5, 5)]
    public void FromPersisted_projects_a_persisted_codex_row_with_a_stray_cache_value_as_unknown(
        int? cacheCreation, int? cacheRead)
    {
        Assert.Null(AgentTokenUsageEvidence.FromPersisted(
            AgentProvider.Codex, 2400, 120, cacheCreation, cacheRead, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion));
    }

    // Preserved: this rule is Codex-schema-specific. A persisted Claude row with the same cache
    // values remains fully known, exactly as before.
    [Fact]
    public void FromPersisted_still_accepts_a_claude_cache_breakdown()
    {
        Assert.Equal(
            TestTokenUsage.Reported,
            AgentTokenUsageEvidence.FromPersisted(
                AgentProvider.ClaudeCode, 1200, 345, 67, 890, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion));
    }

    // The Domain transition itself can never be reached with this shape: the sealed evidence type
    // is only ever constructed via Create (which throws here) or FromPersisted (which returns
    // null above) — there is no path that lets Attempt.CompleteAgent see it.
    [Fact]
    public void Create_rejects_codex_evidence_with_a_cache_breakdown_before_it_can_reach_any_domain_transition()
    {
        var error = Assert.Throws<ArgumentException>(() => AgentTokenUsageEvidence.Create(
            2400, 120, 5, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion));

        Assert.Contains(nameof(AgentTokenUsageEvidenceViolation.CodexUsageCannotIncludeACacheBreakdown), error.Message);
    }

    [Fact]
    public void A_codex_attempt_rejects_claude_usage_before_any_transition_mutation()
    {
        var attempt = ClaimDispatchedPlanner();

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgent(
            AgentOutcome.ProviderInvocationFailed, null, BaseTime, TestProcessEvidence.CleanExit, TestTokenUsage.Reported));

        AssertUntouched(attempt);
    }

    [Fact]
    public void A_codex_attempt_records_usage_reported_through_its_own_proven_schema()
    {
        var attempt = ClaimDispatchedPlanner();

        attempt.CompleteAgent(
            AgentOutcome.Proposed, Fingerprint, BaseTime, TestProcessEvidence.CleanExit, TestTokenUsage.CodexReported);

        Assert.Equal(TestTokenUsage.CodexReported, attempt.GetAgentTokenUsageEvidence());
        Assert.Null(attempt.AgentCacheCreationInputTokens);
        Assert.Null(attempt.AgentCacheReadInputTokens);
    }

    // Sync guard: the token-usage policy reuses AgentProcessEvidencePolicy.IsPreInvocationOutcome
    // instead of a second copy. For every defined outcome, present evidence on a dispatched attempt
    // is rejected exactly when that shared set contains the outcome, so the two rules can never
    // silently drift apart when a new outcome is added.
    [Theory]
    [MemberData(nameof(AllOutcomes))]
    public void Policy_rejects_evidence_exactly_for_the_shared_pre_invocation_outcome_set(AgentOutcome outcome)
    {
        var expected = AgentProcessEvidencePolicy.IsPreInvocationOutcome(outcome)
            ? AgentTokenUsageEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence
            : (AgentTokenUsageEvidenceViolation?)null;

        Assert.Equal(expected, AgentTokenUsageEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, true, TestTokenUsage.Reported));
        Assert.Null(AgentTokenUsageEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, true, null));
        Assert.Null(AgentTokenUsageEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, false, null));
    }

    [Theory]
    [InlineData(AgentOutcome.WorkspaceNoLongerEligible)]
    [InlineData(AgentOutcome.InputAlreadyReviewed)]
    [InlineData(AgentOutcome.InputAlreadyResolved)]
    [InlineData(AgentOutcome.InputAlreadyImplemented)]
    [InlineData(AgentOutcome.InputAlreadyCodeReviewed)]
    [InlineData(AgentOutcome.InputAlreadyCorrected)]
    public void Policy_rejects_evidence_for_every_pre_invocation_outcome_regardless_of_dispatch_state(AgentOutcome outcome)
    {
        Assert.Equal(
            AgentTokenUsageEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence,
            AgentTokenUsageEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, true, TestTokenUsage.Reported));
        Assert.Equal(
            AgentTokenUsageEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence,
            AgentTokenUsageEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, false, TestTokenUsage.Reported));
    }

    [Fact]
    public void Policy_rejects_evidence_for_an_undispatched_attempt()
    {
        Assert.Equal(
            AgentTokenUsageEvidenceViolation.NotDispatched,
            AgentTokenUsageEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, AgentOutcome.SourceChanged, false, TestTokenUsage.Reported));
    }

    // Unlike process evidence, token usage never gates on a clean exit: a success outcome, an
    // InvalidStructuredOutput, and a provider failure are all legitimate carriers, and every one
    // of them is equally valid without usage.
    [Theory]
    [InlineData(AgentOutcome.Proposed)]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.SourceChanged)]
    [InlineData(AgentOutcome.CheckpointEvidenceUnavailable)]
    [InlineData(AgentOutcome.NoChangesProduced)]
    public void Policy_never_requires_usage_and_accepts_it_for_any_post_invocation_outcome(AgentOutcome outcome)
    {
        Assert.Null(AgentTokenUsageEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, true, null));
        Assert.Null(AgentTokenUsageEvidencePolicy.Evaluate(AgentProvider.ClaudeCode, outcome, true, TestTokenUsage.Reported));
    }

    [Fact]
    public void CompleteAgent_records_usage_atomically_with_process_evidence_and_a_success_outcome()
    {
        var attempt = ClaimDispatchedCriticalReview();

        attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, BaseTime.AddSeconds(2), TestProcessEvidence.CleanExit, TestTokenUsage.Reported);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Accepted, attempt.AgentOutcome);
        Assert.Equal(TestProcessEvidence.CleanExit, attempt.GetAgentProcessExecutionEvidence());
        Assert.Equal(1200, attempt.AgentInputTokens);
        Assert.Equal(345, attempt.AgentOutputTokens);
        Assert.Equal(67, attempt.AgentCacheCreationInputTokens);
        Assert.Equal(890, attempt.AgentCacheReadInputTokens);
        Assert.Equal("claude-cli-usage-v1", attempt.AgentTokenUsageSchemaVersion);
        Assert.Equal(TestTokenUsage.Reported, attempt.GetAgentTokenUsageEvidence());
    }

    [Theory]
    [InlineData(ProcessOutcome.Exited, 1)]
    [InlineData(ProcessOutcome.TimedOut, null)]
    [InlineData(ProcessOutcome.Cancelled, null)]
    public void CompleteAgent_records_usage_for_a_provider_failure_with_a_non_clean_process_result(ProcessOutcome outcome, int? exitCode)
    {
        var attempt = ClaimDispatchedCriticalReview();
        var evidence = AgentProcessExecutionEvidence.Create(outcome, exitCode, TimeSpan.FromSeconds(3));

        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, BaseTime, evidence, TestTokenUsage.Reported);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(evidence, attempt.GetAgentProcessExecutionEvidence());
        Assert.Equal(TestTokenUsage.Reported, attempt.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public void CompleteAgent_records_usage_for_a_clean_exit_with_invalid_output()
    {
        var attempt = ClaimDispatchedCriticalReview();

        attempt.CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, BaseTime, TestProcessEvidence.CleanExit, TestTokenUsage.Reported);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(TestTokenUsage.Reported, attempt.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public void CompleteAgent_never_fabricates_usage_when_none_was_reported()
    {
        var attempt = ClaimDispatchedPlanner();

        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, BaseTime, TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        AssertNoUsage(attempt);
    }

    [Fact]
    public void CompleteAgent_keeps_usage_when_source_drift_overrides_the_semantic_outcome()
    {
        var attempt = ClaimDispatchedCriticalReview();

        attempt.CompleteAgent(AgentOutcome.Accepted, "fingerprint-drifted", BaseTime, TestProcessEvidence.CleanExit, TestTokenUsage.Reported);

        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Equal(TestTokenUsage.Reported, attempt.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public void CompleteAgent_rejects_usage_for_an_undispatched_attempt_and_leaves_it_untouched()
    {
        var attempt = ClaimCriticalReview();

        var error = Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.SourceChanged, null, BaseTime, null, TestTokenUsage.Reported));

        Assert.Contains(nameof(AgentTokenUsageEvidenceViolation.NotDispatched), error.Message);
        AssertUntouched(attempt);
    }

    [Fact]
    public void CompleteAgent_rejects_usage_for_a_pre_invocation_outcome_even_when_dispatched()
    {
        var attempt = ClaimDispatchedCriticalReview();

        var error = Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.WorkspaceNoLongerEligible, null, BaseTime, null, TestTokenUsage.Reported));

        Assert.Contains(nameof(AgentTokenUsageEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence), error.Message);
        AssertUntouched(attempt);
    }

    // Token-usage rejection is checked before any mutation, so it can never leave a half-recorded
    // attempt holding valid process evidence but no outcome.
    [Fact]
    public void A_rejected_usage_leaves_otherwise_valid_process_evidence_unrecorded()
    {
        var attempt = ClaimDispatchedCriticalReview();

        var error = Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.InputAlreadyReviewed, null, BaseTime, null, TestTokenUsage.Reported));

        Assert.Contains(nameof(AgentTokenUsageEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence), error.Message);
        AssertUntouched(attempt);
        Assert.Null(attempt.AgentProcessOutcome);
    }

    [Fact]
    public void Usage_is_write_once_because_a_terminal_attempt_cannot_be_completed_again()
    {
        var attempt = ClaimDispatchedCriticalReview();
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, BaseTime, null, TestTokenUsage.Reported);

        var second = AgentTokenUsageEvidence.Create(1, 1, null, null, "claude-cli-usage-v1");
        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, BaseTime.AddSeconds(1), null, second));

        Assert.Equal(TestTokenUsage.Reported, attempt.GetAgentTokenUsageEvidence());
        Assert.Equal(BaseTime, attempt.CompletedAtUtc);
    }

    [Fact]
    public void CompleteImplementation_records_usage_with_Implemented_and_with_a_provider_failure()
    {
        var implemented = ClaimDispatchedImplementation();
        implemented.CompleteImplementation(
            AgentOutcome.Implemented, Guid.NewGuid(), BaseTime, TestProcessEvidence.CleanExit, TestTokenUsage.Reported);
        Assert.Equal(AttemptStatus.Completed, implemented.Status);
        Assert.Equal(TestTokenUsage.Reported, implemented.GetAgentTokenUsageEvidence());

        var failed = ClaimDispatchedImplementation();
        var nonZero = AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 2, TimeSpan.FromSeconds(1));
        failed.CompleteImplementation(AgentOutcome.ProviderInvocationFailed, null, BaseTime, nonZero, TestTokenUsage.InputOutputOnly);
        Assert.Equal(AttemptStatus.Failed, failed.Status);
        Assert.Equal(TestTokenUsage.InputOutputOnly, failed.GetAgentTokenUsageEvidence());
        Assert.Null(failed.AgentCacheCreationInputTokens);
        Assert.Null(failed.AgentCacheReadInputTokens);
    }

    [Fact]
    public void CompleteImplementation_rejects_usage_for_InputAlreadyImplemented()
    {
        var attempt = ClaimDispatchedImplementation();

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteImplementation(AgentOutcome.InputAlreadyImplemented, null, BaseTime, null, TestTokenUsage.Reported));

        AssertUntouched(attempt);
    }

    [Fact]
    public void CompleteReviewCorrection_records_usage_with_CorrectionApplied_and_rejects_it_for_InputAlreadyCorrected()
    {
        var applied = ClaimDispatchedCorrection();
        applied.CompleteReviewCorrection(
            AgentOutcome.CorrectionApplied, Guid.NewGuid(), BaseTime, TestProcessEvidence.CleanExit, TestTokenUsage.Reported);
        Assert.Equal(TestTokenUsage.Reported, applied.GetAgentTokenUsageEvidence());

        var raced = ClaimDispatchedCorrection();
        Assert.Throws<InvalidOperationException>(
            () => raced.CompleteReviewCorrection(AgentOutcome.InputAlreadyCorrected, null, BaseTime, null, TestTokenUsage.Reported));
        AssertUntouched(raced);
    }

    [Fact]
    public void Interrupt_never_invents_usage_for_a_dispatched_attempt()
    {
        var attempt = ClaimDispatchedImplementation();

        attempt.Interrupt(BaseTime.AddMinutes(1));

        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        AssertNoUsage(attempt);
    }

    [Fact]
    public void Non_agent_attempts_never_expose_token_usage()
    {
        var simulated = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);

        Assert.Null(simulated.GetAgentTokenUsageEvidence());
        Assert.Null(simulated.AgentInputTokens);
    }

    private static void AssertNoUsage(Attempt attempt)
    {
        Assert.Null(attempt.AgentInputTokens);
        Assert.Null(attempt.AgentOutputTokens);
        Assert.Null(attempt.AgentCacheCreationInputTokens);
        Assert.Null(attempt.AgentCacheReadInputTokens);
        Assert.Null(attempt.AgentTokenUsageSchemaVersion);
        Assert.Null(attempt.GetAgentTokenUsageEvidence());
    }

    private static void AssertUntouched(Attempt attempt)
    {
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.AgentProcessOutcome);
        AssertNoUsage(attempt);
    }

    private static Attempt ClaimPlanner() => Attempt.ClaimAgent(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, BaseTime, 1);

    private static Attempt ClaimDispatchedPlanner()
    {
        var attempt = ClaimPlanner();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        return attempt;
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
