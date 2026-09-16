namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// Immutable, redacted-best-effort output evidence for one verification execution. Its path is
/// always relative to the application-owned artifact root and is never a project path.
/// </summary>
public sealed class VerificationOutputArtifact
{
    private VerificationOutputArtifact()
    {
    }

    public static VerificationOutputArtifact Record(
        Guid id,
        Guid verificationExecutionId,
        VerificationOutputPurpose purpose,
        string relativeStoragePath,
        string contentHash,
        long byteLength,
        bool? truncated,
        VerificationOutputCaptureOutcome captureOutcome,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeStoragePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        if (captureOutcome == VerificationOutputCaptureOutcome.CapturedWithKnownTruncation && !truncated.HasValue)
        {
            throw new ArgumentException("Known-truncation output must include a truncation value.", nameof(truncated));
        }

        if (captureOutcome == VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption && truncated.HasValue)
        {
            throw new ArgumentException("Recovered output must leave truncation unknown.", nameof(truncated));
        }

        return new VerificationOutputArtifact
        {
            Id = id,
            VerificationExecutionId = verificationExecutionId,
            Purpose = purpose,
            RelativeStoragePath = relativeStoragePath,
            ContentHash = contentHash,
            ByteLength = byteLength,
            Truncated = truncated,
            CaptureOutcome = captureOutcome,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid VerificationExecutionId { get; private set; }

    public VerificationOutputPurpose Purpose { get; private set; }

    public string RelativeStoragePath { get; private set; } = string.Empty;

    public string ContentHash { get; private set; } = string.Empty;

    public long ByteLength { get; private set; }

    public bool? Truncated { get; private set; }

    public VerificationOutputCaptureOutcome CaptureOutcome { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
