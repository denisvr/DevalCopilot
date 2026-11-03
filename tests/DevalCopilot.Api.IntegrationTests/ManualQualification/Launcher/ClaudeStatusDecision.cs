namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The closed decision of the two observations, exactly the plan's table. None of them activates anything or claims that
/// credentials or model access match.</summary>
public enum ClaudeStatusDecision
{
    /// <summary>Not decided: the sequence stopped before two coherent observations existed.</summary>
    Stopped = 0,

    /// <summary>Both reported authenticated: supports proceeding to lot B preparation and review.</summary>
    BothAuthenticated = 1,

    /// <summary>The ordinary context reported authenticated and the production-filtered one logged out: a context discrepancy.</summary>
    OrdinaryAuthenticatedFilteredLoggedOut = 2,

    /// <summary>Both reported logged out: an owner-managed authentication prerequisite remains.</summary>
    BothLoggedOut = 3,

    /// <summary>The ordinary context reported logged out and the filtered one authenticated: stop with no causal conclusion.</summary>
    ReverseDiscrepancy = 4,
}
