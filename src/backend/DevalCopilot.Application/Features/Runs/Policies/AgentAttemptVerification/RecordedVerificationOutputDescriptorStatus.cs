namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>The closed state of one allowlisted output stream of a recorded verification member. <see cref="NotRecorded"/>: no row exists
/// for that exact execution and stream. <see cref="Unavailable"/>: a row exists but its metadata cannot be admitted. Neither carries any
/// other fact. <see cref="Recorded"/>: an admitted artifact identity, byte length and capture tuple.</summary>
public enum RecordedVerificationOutputDescriptorStatus
{
    NotRecorded,
    Unavailable,
    Recorded,
}
