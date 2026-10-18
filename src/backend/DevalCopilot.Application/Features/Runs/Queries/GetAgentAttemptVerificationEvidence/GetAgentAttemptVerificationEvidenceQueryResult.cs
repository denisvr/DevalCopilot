namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

/// <summary>
/// <see cref="AgentAttemptVerificationEvidenceStatus.Unavailable"/>: no header, no checkpoint, no count and no members.
/// <see cref="AgentAttemptVerificationEvidenceStatus.Recorded"/>: the header, the recorded checkpoint, the exact count from 1 to 32 and
/// every member in recorded order, without pagination or omission.
/// </summary>
public sealed record GetAgentAttemptVerificationEvidenceQueryResult(
    Guid RunId,
    Guid AttemptId,
    AgentAttemptVerificationEvidenceStatus Status,
    AgentAttemptVerificationEvidenceHeader? Attempt,
    AgentAttemptVerificationEvidenceCheckpoint? Checkpoint,
    int? MemberTotalCount,
    IReadOnlyList<AgentAttemptVerificationMember> Members)
{
    public static GetAgentAttemptVerificationEvidenceQueryResult Unavailable(Guid runId, Guid attemptId) =>
        new(runId, attemptId, AgentAttemptVerificationEvidenceStatus.Unavailable, null, null, null, []);
}
