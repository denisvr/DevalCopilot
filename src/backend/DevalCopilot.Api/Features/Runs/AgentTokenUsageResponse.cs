using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs;

/// <summary>
/// Provider-reported token usage for one Agent attempt, exposed as a sibling of the attempt's
/// semantic <c>outcome</c> and its host-measured <c>processExecution</c> and never a replacement for
/// either. Every member is null when the evidence is absent or unknown — including every attempt
/// whose provider has no proven usage contract. The cache members may also be null alone for a
/// provider contract without a cache breakdown. The internal parsing-contract schema version is
/// deliberately never exposed. Never carries a path, argument, environment value, output,
/// manifest, session identifier, or credential.
/// </summary>
public sealed record AgentTokenUsageResponse(
    int? InputTokens,
    int? OutputTokens,
    int? CacheCreationInputTokens,
    int? CacheReadInputTokens)
{
    /// <summary>An existing attempt always carries this object; its members stay null while the
    /// evidence is absent or unknown.</summary>
    public static AgentTokenUsageResponse FromAttempt(AgentTokenUsageEvidence? evidence) =>
        new(evidence?.InputTokens, evidence?.OutputTokens, evidence?.CacheCreationInputTokens, evidence?.CacheReadInputTokens);

    /// <summary>Null only when there is no attempt at all.</summary>
    public static AgentTokenUsageResponse? FromDomain(bool hasAttempt, AgentTokenUsageEvidence? evidence) =>
        hasAttempt ? FromAttempt(evidence) : null;
}
