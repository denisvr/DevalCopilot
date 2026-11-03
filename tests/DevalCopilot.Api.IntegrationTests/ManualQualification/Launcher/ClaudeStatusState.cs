namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>What one <c>claude auth status</c> observation reported, closed. It is evidence of the observed context only, never
/// proof of quota, model access, credential validity or a historical cause. <see cref="Unknown"/> covers every output that was not a
/// complete, coherent report: malformed, truncated, contradicting its exit code, or never observed.</summary>
public enum ClaudeStatusState
{
    Unknown = 0,
    ReportedAuthenticated = 1,
    ReportedLoggedOut = 2,
}
