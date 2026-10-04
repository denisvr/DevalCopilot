namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// A closed reason why provider-reported model context-limit evidence cannot be recorded. Evaluated by
/// <see cref="AgentModelContextLimitsEvidence.Validate"/> and <see cref="AgentModelContextLimitsEvidencePolicy"/> and
/// enforced by every Agent completion transition on <see cref="Attempt"/>.
/// </summary>
public enum AgentModelContextLimitsEvidenceViolation
{
    /// <summary>No model entry was supplied.</summary>
    NoModels = 0,

    /// <summary>More than <see cref="AgentModelContextLimitsEvidence.MaxModels"/> entries were supplied.</summary>
    TooManyModels = 1,

    /// <summary>An identifier is not 1 to <see cref="AgentModelContextLimitsEvidence.MaxModelIdLength"/> ASCII
    /// characters matching <c>[A-Za-z0-9][A-Za-z0-9._-]*</c>.</summary>
    InvalidModelId = 2,

    /// <summary>The same identifier appears more than once (ordinal comparison).</summary>
    DuplicateModelId = 3,

    /// <summary>A context-window limit is not a positive number.</summary>
    InvalidContextWindowTokens = 4,

    /// <summary>A maximum-output limit is not a positive number.</summary>
    InvalidMaxOutputTokens = 5,

    /// <summary>A maximum-output limit is greater than the same entry's context-window limit.</summary>
    MaxOutputExceedsContextWindow = 6,

    /// <summary>The parsing-contract source tag is empty, longer than
    /// <see cref="AgentModelContextLimitsEvidence.MaxSourceLength"/> characters, or not printable ASCII.</summary>
    InvalidSource = 7,

    /// <summary>The normalized snapshot would exceed <see cref="AgentModelContextLimitsEvidence.MaxSerializedBytes"/>
    /// UTF-8 bytes.</summary>
    TooLarge = 8,

    /// <summary>Evidence was supplied for an attempt that was never dispatched to its provider.</summary>
    NotDispatched = 9,

    /// <summary>Evidence was supplied for an outcome that is always detected immediately before the provider is ever
    /// invoked — see <see cref="AgentProcessEvidencePolicy.IsPreInvocationOutcome"/>.</summary>
    PreInvocationOutcomeCannotCarryEvidence = 10,

    /// <summary>The attempt provider and parsing-contract source are not an evidenced pair.</summary>
    UnsupportedProviderSource = 11,
}
