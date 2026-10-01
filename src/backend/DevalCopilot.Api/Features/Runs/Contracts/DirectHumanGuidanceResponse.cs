using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.Contracts;

/// <summary>
/// The direct human guidance recorded on an Agent attempt's own immutable snapshot. <c>State</c> is exactly one of
/// <c>NotRecorded</c> (no direct guidance was recorded; this says nothing about whether any text was ever submitted, so
/// it is the state of historical and new unguided attempts alike), <c>Provided</c> (the accepted text is in <c>Text</c>,
/// at most 600 characters) or <c>Unknown</c> (the recorded facts disagree; no text is exposed). It is separate from the
/// authorization of an extra correction, and it states what the host supplied to the attempt's sealed context, never
/// that a provider followed it.
/// </summary>
public sealed record DirectHumanGuidanceResponse(string State, string? Text)
{
    /// <summary>Null when there is no fact (no attempt, or an attempt outside the Claude mutation paths).</summary>
    public static DirectHumanGuidanceResponse? FromDomain(DirectHumanGuidanceFact? fact) =>
        fact is null ? null : new DirectHumanGuidanceResponse(StateName(fact.Evidence), fact.Text);

    private static string StateName(DirectHumanGuidanceEvidence evidence) => evidence switch
    {
        DirectHumanGuidanceEvidence.NotRecorded => "NotRecorded",
        DirectHumanGuidanceEvidence.Provided => "Provided",
        _ => "Unknown",
    };
}
