namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;

/// <summary>The recorded verification membership of one CodeReviewer attempt (ADR-0052), as read now: recorded membership and the
/// referenced rows, not proof that they still match a claim-time snapshot, a current eligibility or an approval. <c>Status</c> is
/// <c>Unavailable</c> (no attempt, checkpoint or count and no members; which proof failed is not disclosed) or <c>Recorded</c> (the
/// attempt, its recorded checkpoint, the exact count from 1 to 32 and every member in recorded order).</summary>
public sealed record GetAgentAttemptVerificationEvidenceResponse(
    Guid RunId,
    Guid AttemptId,
    string Status,
    AgentAttemptVerificationHeaderResponse? Attempt,
    AgentAttemptVerificationCheckpointResponse? Checkpoint,
    int? MemberTotalCount,
    IReadOnlyList<AgentAttemptVerificationMemberResponse> Members);
