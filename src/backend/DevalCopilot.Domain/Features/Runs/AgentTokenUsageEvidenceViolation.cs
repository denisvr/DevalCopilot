namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// A closed reason why provider-reported Agent token-usage evidence cannot be recorded. Evaluated
/// by <see cref="AgentTokenUsageEvidence.Validate"/> and <see cref="AgentTokenUsageEvidencePolicy"/>
/// and enforced by every Agent completion transition on <see cref="Attempt"/>.
/// </summary>
public enum AgentTokenUsageEvidenceViolation
{
    /// <summary>The reported input token count is negative.</summary>
    NegativeInputTokens = 0,

    /// <summary>The reported output token count is negative.</summary>
    NegativeOutputTokens = 1,

    /// <summary>The reported cache-creation input token count is present and negative.</summary>
    NegativeCacheCreationInputTokens = 2,

    /// <summary>The reported cache-read input token count is present and negative.</summary>
    NegativeCacheReadInputTokens = 3,

    /// <summary>The parsing-contract schema version is blank or longer than
    /// <see cref="AgentTokenUsageEvidence.MaxSchemaVersionLength"/> characters.</summary>
    InvalidSchemaVersion = 4,

    /// <summary>Evidence was supplied for an attempt that was never dispatched to its provider — no
    /// provider invocation can have reported usage for it.</summary>
    NotDispatched = 5,

    /// <summary>Evidence was supplied for an outcome that is, by its own Domain meaning, always
    /// detected immediately before the provider is ever invoked — see
    /// <see cref="AgentProcessEvidencePolicy.IsPreInvocationOutcome"/>.</summary>
    PreInvocationOutcomeCannotCarryEvidence = 6,

    /// <summary>The attempt provider and parsing-contract tag are not an evidenced pair.</summary>
    UnsupportedProviderSchema = 7,

    /// <summary>The schema version is Codex's own proven contract
    /// (<see cref="AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion"/>), yet a cache-creation or
    /// cache-read input token count is present. Codex's own contract never carries either — see
    /// <see cref="AgentTokenUsageEvidence.CacheCreationInputTokens"/> and
    /// <see cref="AgentTokenUsageEvidence.CacheReadInputTokens"/> — so a non-null value under that
    /// schema is not evidence this contract proves, never an authoritative cache breakdown.</summary>
    CodexUsageCannotIncludeACacheBreakdown = 8,
}
