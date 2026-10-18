using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>
/// The admitted metadata of one allowlisted output stream, as read now. Only <see cref="RecordedVerificationOutputDescriptorStatus.Recorded"/>
/// carries values: a nonempty artifact identity, a nonnegative safe-integer byte length and either
/// <see cref="VerificationOutputCaptureOutcome.CapturedWithKnownTruncation"/> with a known <see cref="Truncated"/> or
/// <see cref="VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption"/> with an unknown (null) one. It never carries a path or
/// a hash.
/// </summary>
public sealed record RecordedVerificationOutputDescriptor(
    RecordedVerificationOutputDescriptorStatus Status,
    Guid? ArtifactId,
    long? ByteLength,
    VerificationOutputCaptureOutcome? CaptureOutcome,
    bool? Truncated)
{
    public static readonly RecordedVerificationOutputDescriptor NotRecorded =
        new(RecordedVerificationOutputDescriptorStatus.NotRecorded, null, null, null, null);

    public static readonly RecordedVerificationOutputDescriptor Unavailable =
        new(RecordedVerificationOutputDescriptorStatus.Unavailable, null, null, null, null);
}
