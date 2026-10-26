namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationInspection;

/// <summary>
/// The admitted metadata of one allowlisted sealed output stream. <c>NotRecorded</c> (no row for that execution and stream) and
/// <c>Unavailable</c> (a row whose metadata cannot be admitted) carry null for every other field. <c>Recorded</c> carries a nonempty
/// artifact identity, a nonnegative safe-integer byte length and either <c>CapturedWithKnownTruncation</c> with a Boolean
/// <c>Truncated</c> or <c>RecoveredAfterHostInterruption</c> with a null (unknown) one. It never carries a path or a hash.
/// </summary>
public sealed record ProjectVerificationInspectionStreamResponse(
    string Status, Guid? ArtifactId, long? ByteLength, string? CaptureOutcome, bool? Truncated);
