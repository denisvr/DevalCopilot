namespace DevalCopilot.Domain.Features.Runs;

/// <summary>Which existing plan authority admitted the pinned plan of a local execution sequence (ADR-0060).</summary>
public enum LocalExecutionPlanAuthority
{
    /// <summary>An explicitly Accepted Planner root proposal.</summary>
    AcceptedRoot = 0,

    /// <summary>An explicitly Accepted depth-one revision.</summary>
    AcceptedRevision = 1,

    /// <summary>ADR-0016's separately Human-authorized final depth-two revision.</summary>
    HumanAuthorizedRevision = 2,
}
