namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

/// <summary>The recorded terminal status of a member's execution. Both imply an Exited process: Passed with exit code 0, Failed with a
/// nonzero exit code. A historical Passed is never a current approval.</summary>
public enum AgentAttemptVerificationMemberStatus
{
    Passed,
    Failed,
}
