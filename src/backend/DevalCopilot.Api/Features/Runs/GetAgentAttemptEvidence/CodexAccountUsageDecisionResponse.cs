using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptEvidence;

/// <summary>
/// The decision that stopped one Codex attempt before it was dispatched because of the run's account-usage stop (ADR-0025). <c>State</c>
/// is <c>Recorded</c> (every other member is present as the validated decision) or <c>Unavailable</c> (the attempt's outcome says it
/// was stopped but its stored decision could not be proved, so nothing else is shown). <c>Decision</c> is <c>Reached</c> or
/// <c>Unavailable</c>; <c>Reason</c> is one fixed word, never provider text. <c>RetrievedAtUtc</c> is the host's own clock reading
/// when the account was observed, not the age of the provider's data. <c>Windows</c> are only the validated percentages the decision
/// used. A local guard over a provider-reported percentage: never account access, readiness, remaining quota or live capacity. The whole
/// member is null for an attempt that was not stopped, and absence is never a statement that an account was below a threshold.
/// </summary>
public sealed record CodexAccountUsageDecisionResponse(
    string State,
    string? Decision,
    string? Reason,
    int? ThresholdPercent,
    DateTimeOffset? RetrievedAtUtc,
    IReadOnlyList<CodexAccountUsageWindowResponse> Windows)
{
    public static CodexAccountUsageDecisionResponse? FromDomain(CodexAccountUsageDecisionFact? fact)
    {
        if (fact is null)
        {
            return null;
        }

        if (!fact.Recorded || fact.Decision is not { } decision)
        {
            return new CodexAccountUsageDecisionResponse("Unavailable", null, null, null, null, []);
        }

        return new CodexAccountUsageDecisionResponse(
            "Recorded",
            decision.Kind == CodexAccountUsageDecisionKind.Reached ? "Reached" : "Unavailable",
            ReasonName(decision.Reason),
            decision.ThresholdPercent,
            decision.RetrievedAtUtc,
            decision.Windows
                .Select(window => new CodexAccountUsageWindowResponse(
                    window.BucketId, window.Window == CodexAccountUsageWindowKind.Primary ? "Primary" : "Secondary", window.UsedPercent))
                .ToArray());
    }

    private static string ReasonName(CodexAccountUsageDecisionReason reason) => reason switch
    {
        CodexAccountUsageDecisionReason.ThresholdReached => "ThresholdReached",
        CodexAccountUsageDecisionReason.ProviderReportedLimitReached => "ProviderReportedLimitReached",
        CodexAccountUsageDecisionReason.EvidenceUnavailable => "EvidenceUnavailable",
        CodexAccountUsageDecisionReason.EvidenceExpired => "EvidenceExpired",
        _ => "ThresholdUnusable",
    };
}
