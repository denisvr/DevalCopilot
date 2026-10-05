namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The fixed, closed reason of a stop decision. Provider text is never carried.</summary>
public enum CodexAccountUsageDecisionReason
{
    /// <summary>A validated window reported a used percentage at or above the claimed threshold (equality stops).</summary>
    ThresholdReached = 1,

    /// <summary>The provider reported a non-null reached-limit state, whatever the percentages were.</summary>
    ProviderReportedLimitReached = 2,

    /// <summary>The observation was invalid, partial, duplicated or unavailable.</summary>
    EvidenceUnavailable = 3,

    /// <summary>The observation lay outside the current read interval, was older than the host freshness limit, was dated in the
    /// future, or a known reset had already passed.</summary>
    EvidenceExpired = 4,

    /// <summary>The attempt's stored threshold snapshot was malformed, so no number could be claimed.</summary>
    ThresholdUnusable = 5,
}
