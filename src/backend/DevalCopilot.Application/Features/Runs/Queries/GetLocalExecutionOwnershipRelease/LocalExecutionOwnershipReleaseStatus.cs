namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;

/// <summary>The closed status of one step's ownership release reading (ADR-0065). Only Available carries a candidate and only Released a
/// receipt.</summary>
public enum LocalExecutionOwnershipReleaseStatus
{
    NotEligible = 0,
    Unavailable = 1,
    Available = 2,
    Released = 3,
}
