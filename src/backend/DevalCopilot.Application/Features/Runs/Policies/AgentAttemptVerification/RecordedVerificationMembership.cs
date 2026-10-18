namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>The whole proved verification membership of one CodeReviewer attempt: its immutable recorded identity, the checkpoint it
/// recorded and every member in recorded order (1 to 32). It states recorded membership and referenced rows as read now, never that they
/// still match a claim-time snapshot, a current eligibility or an approval.</summary>
internal sealed record RecordedVerificationMembership(
    Guid AttemptId,
    int AttemptNumber,
    CoherentAgentAttemptIdentity Identity,
    Guid CheckpointId,
    int CheckpointNumber,
    IReadOnlyList<RecordedVerificationMember> Members);
