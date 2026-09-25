using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class AgentTokenUsageRecordingTests
{
    [Theory]
    [InlineData(-1, 0, 0, 0, "claude-cli-usage-v1")]
    [InlineData(0, -1, 0, 0, "claude-cli-usage-v1")]
    [InlineData(0, 0, -1, 0, "claude-cli-usage-v1")]
    [InlineData(0, 0, 0, -1, "claude-cli-usage-v1")]
    [InlineData(0, 0, 0, 0, "")]
    [InlineData(0, 0, 0, 0, " ")]
    public void Validate_rejects_every_invalid_usage_shape(int input, int output, int? cacheCreation, int? cacheRead, string schemaVersion)
    {
        var error = AgentTokenUsageRecording.Validate(
            new AgentTokenUsage(input, output, cacheCreation, cacheRead, schemaVersion),
            AgentProvider.ClaudeCode,
            AgentOutcome.ProviderInvocationFailed,
            dispatched: true,
            out var domainEvidence);

        Assert.Equal(AgentTokenUsageRecording.InvalidEvidenceCode, error?.Code);
        Assert.Null(domainEvidence);
    }

    [Fact]
    public void Validate_rejects_an_oversized_schema_version()
    {
        var error = AgentTokenUsageRecording.Validate(
            new AgentTokenUsage(1, 1, null, null, new string('v', AgentTokenUsageEvidence.MaxSchemaVersionLength + 1)),
            AgentProvider.ClaudeCode,
            AgentOutcome.ProviderInvocationFailed,
            dispatched: true,
            out var domainEvidence);

        Assert.Equal(AgentTokenUsageRecording.InvalidEvidenceCode, error?.Code);
        Assert.Null(domainEvidence);
    }

    [Fact]
    public void Validate_rejects_usage_for_an_undispatched_attempt()
    {
        var error = AgentTokenUsageRecording.Validate(TestTokenUsage.Reported, AgentProvider.ClaudeCode, AgentOutcome.SourceChanged, false, out var evidence);

        Assert.Equal(AgentTokenUsageRecording.EvidenceWithoutDispatchCode, error?.Code);
        Assert.Null(evidence);
    }

    [Theory]
    [InlineData(AgentOutcome.WorkspaceNoLongerEligible)]
    [InlineData(AgentOutcome.InputAlreadyReviewed)]
    [InlineData(AgentOutcome.InputAlreadyResolved)]
    [InlineData(AgentOutcome.InputAlreadyImplemented)]
    [InlineData(AgentOutcome.InputAlreadyCodeReviewed)]
    [InlineData(AgentOutcome.InputAlreadyCorrected)]
    public void Validate_rejects_usage_for_every_pre_invocation_outcome_regardless_of_dispatch_state(AgentOutcome outcome)
    {
        var dispatchedError = AgentTokenUsageRecording.Validate(TestTokenUsage.Reported, AgentProvider.ClaudeCode, outcome, true, out var dispatchedEvidence);
        var undispatchedError = AgentTokenUsageRecording.Validate(TestTokenUsage.Reported, AgentProvider.ClaudeCode, outcome, false, out _);

        Assert.Equal(AgentTokenUsageRecording.PreInvocationOutcomeCannotCarryEvidenceCode, dispatchedError?.Code);
        Assert.Equal(AgentTokenUsageRecording.PreInvocationOutcomeCannotCarryEvidenceCode, undispatchedError?.Code);
        Assert.Null(dispatchedEvidence);
        Assert.Null(AgentTokenUsageRecording.Validate(null, AgentProvider.ClaudeCode, outcome, false, out _));
    }

    // Unlike AgentProcessEvidenceRecording there is no clean-exit requirement: absent usage is
    // accepted for every outcome, including semantic successes, and present usage is accepted for
    // success and failure outcomes alike.
    [Theory]
    [InlineData(AgentOutcome.Proposed)]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.Implemented)]
    [InlineData(AgentOutcome.CorrectionApplied)]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.CheckpointEvidenceUnavailable)]
    public void Validate_never_requires_usage_and_translates_supplied_usage_to_domain_evidence(AgentOutcome outcome)
    {
        Assert.Null(AgentTokenUsageRecording.Validate(null, AgentProvider.ClaudeCode, outcome, true, out var absent));
        Assert.Null(absent);

        Assert.Null(AgentTokenUsageRecording.Validate(TestTokenUsage.Reported, AgentProvider.ClaudeCode, outcome, true, out var present));
        Assert.Equal(TestTokenUsage.Evidence, present);
    }

    [Fact]
    public void Validate_accepts_usage_without_a_cache_breakdown()
    {
        var error = AgentTokenUsageRecording.Validate(
            new AgentTokenUsage(10, 20, null, null, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion),
            AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, true, out var evidence);

        Assert.Null(error);
        Assert.Equal(AgentTokenUsageEvidence.Create(10, 20, null, null, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion), evidence);
    }

    [Theory]
    [InlineData(AgentProvider.Codex, "claude-cli-usage-v1")]
    [InlineData(AgentProvider.ClaudeCode, "future-provider-usage-v1")]
    [InlineData((AgentProvider)999, "claude-cli-usage-v1")]
    public void Validate_rejects_unproven_provider_schema_pairs(AgentProvider provider, string schemaVersion)
    {
        var error = AgentTokenUsageRecording.Validate(
            new AgentTokenUsage(1, 2, null, null, schemaVersion),
            provider, AgentOutcome.ProviderInvocationFailed, true, out var evidence);

        Assert.Equal(AgentTokenUsageRecording.InvalidEvidenceCode, error?.Code);
        Assert.Null(evidence);
    }

    [Fact]
    public void Validate_keeps_absent_codex_usage_unknown_without_blocking_the_result()
    {
        Assert.Null(AgentTokenUsageRecording.Validate(
            null, AgentProvider.Codex, AgentOutcome.ProviderInvocationFailed, true, out var evidence));
        Assert.Null(evidence);
    }

    [Fact]
    public void Validate_accepts_codex_usage_reported_through_its_own_proven_schema()
    {
        var error = AgentTokenUsageRecording.Validate(
            TestTokenUsage.CodexReported, AgentProvider.Codex, AgentOutcome.ProviderInvocationFailed, true, out var evidence);

        Assert.Null(error);
        Assert.Equal(TestTokenUsage.CodexEvidence, evidence);
        Assert.Null(evidence!.CacheCreationInputTokens);
        Assert.Null(evidence.CacheReadInputTokens);
    }

    [Theory]
    [InlineData(5, null)]
    [InlineData(null, 5)]
    [InlineData(5, 5)]
    public void Validate_rejects_codex_usage_with_a_cache_breakdown_with_zero_mutation(int? cacheCreation, int? cacheRead)
    {
        var error = AgentTokenUsageRecording.Validate(
            new AgentTokenUsage(2400, 120, cacheCreation, cacheRead, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion),
            AgentProvider.Codex, AgentOutcome.ProviderInvocationFailed, true, out var evidence);

        Assert.Equal(AgentTokenUsageRecording.InvalidEvidenceCode, error?.Code);
        Assert.Null(evidence);
    }

    // Preserved: this rule is Codex-schema-specific and never touches Claude's own cache behavior.
    [Fact]
    public void Validate_still_accepts_a_claude_cache_breakdown()
    {
        var error = AgentTokenUsageRecording.Validate(
            TestTokenUsage.Reported, AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, true, out var evidence);

        Assert.Null(error);
        Assert.Equal(TestTokenUsage.Evidence, evidence);
    }
}
