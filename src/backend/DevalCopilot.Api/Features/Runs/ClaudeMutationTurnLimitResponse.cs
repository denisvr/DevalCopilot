using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs;

/// <summary>
/// A Claude agentic-turn-limit <em>request</em>, exposed as either an Agent attempt's own immutable
/// record or the Run's current saved request. <c>State</c> is exactly one of <c>NotRecorded</c> (a legacy
/// attempt that recorded no request), <c>NotRequested</c> (no request is saved or snapshotted), <c>Requested</c>
/// (the saved or snapshotted request in <c>MaxTurns</c>; this says nothing about dispatch, which a versioned adapter
/// decides separately), or <c>Unknown</c>
/// (the recorded facts disagree). <c>MaxTurns</c> is present only for <c>Requested</c>. Never a measured or
/// observed turn count, an account or host-enforced ceiling, or an observed unlimited capacity; the
/// provider may reject or adjust the request.
/// </summary>
public sealed record ClaudeMutationTurnLimitResponse(string State, int? MaxTurns)
{
    /// <summary>Null when there is no fact (no attempt, or an attempt outside the Claude mutation paths).</summary>
    public static ClaudeMutationTurnLimitResponse? FromDomain(ClaudeMutationTurnLimitFact? fact) =>
        fact is null ? null : new ClaudeMutationTurnLimitResponse(StateName(fact.Evidence), fact.MaxTurns);

    private static string StateName(ClaudeMutationTurnLimitEvidence evidence) => evidence switch
    {
        ClaudeMutationTurnLimitEvidence.NotRecorded => "NotRecorded",
        ClaudeMutationTurnLimitEvidence.NotRequested => "NotRequested",
        ClaudeMutationTurnLimitEvidence.Requested => "Requested",
        _ => "Unknown",
    };
}
