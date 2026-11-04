namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Whether a comparison of public or stored evidence with the stored attempt was actually made, and with what result.
/// <see cref="NotAttempted"/> means it was never made (nothing was enumerated, or the request ceiling of the stage was reached or an
/// earlier request of the stage failed); it is neither agreement nor disagreement. A comparison made against an observed empty set
/// is a performed one. <see cref="Unavailable"/> means the request was made and failed or was cancelled, so nothing is known.</summary>
public enum EvidenceRouteComparisonStatus
{
    NotAttempted = 0,
    Agrees = 1,
    Disagrees = 2,
    Unavailable = 3,
}
