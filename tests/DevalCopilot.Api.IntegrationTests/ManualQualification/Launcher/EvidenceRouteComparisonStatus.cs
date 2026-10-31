namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Whether the public message-evidence route was actually compared with the stored attempt. A comparison is made only for
/// one uniquely selected produced message; without one it stays <see cref="NotAttempted"/>, which is neither agreement nor
/// disagreement.</summary>
public enum EvidenceRouteComparisonStatus
{
    NotAttempted = 0,
    Agrees = 1,
    Disagrees = 2,
}
