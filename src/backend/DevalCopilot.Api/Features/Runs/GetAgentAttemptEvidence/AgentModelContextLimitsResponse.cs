using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptEvidence;

/// <summary>
/// The model identifiers the provider listed in its own result for one concluded Agent attempt and the context-window and
/// maximum-output limits it reported for each, ordered ordinally by identifier. Historical, provider-reported observation
/// stored when the attempt concluded: never remaining context, a fullness measure, a live capability, an eligibility
/// decision, or proof that a listed model was used. The whole member is null when the evidence was not recorded, is
/// unknown, or the attempt has none. The internal parsing-contract source is deliberately never exposed.
/// </summary>
public sealed record AgentModelContextLimitsResponse(IReadOnlyList<AgentModelContextLimitResponse> Models)
{
    /// <summary>Null when there is no evidence.</summary>
    public static AgentModelContextLimitsResponse? FromDomain(AgentModelContextLimitsEvidence? evidence) =>
        evidence is null
            ? null
            : new AgentModelContextLimitsResponse(
                evidence.Models
                    .Select(model => new AgentModelContextLimitResponse(model.ModelId, model.ContextWindowTokens, model.MaxOutputTokens))
                    .ToArray());
}
