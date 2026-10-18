namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

/// <summary>The closed state of one attempt's recorded verification membership. <see cref="Unavailable"/> covers an unprovable identity,
/// an unsupported contract, no membership, more memberships than this inspection judges and any incoherent or unprovable fact, and
/// discloses neither which nor a count; it is not a declaration that stored data is invalid. <see cref="Recorded"/> is a whole, proved
/// membership of 1 to 32 members.</summary>
public enum AgentAttemptVerificationEvidenceStatus
{
    Unavailable,
    Recorded,
}
