using DevalCopilot.Domain.Features.Processes;

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
        DateTimeOffset createdAtUtc,
        OutputCaptureCompletion? captureCompletion = null)
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

        // ADR-0072: only a same-host capture records how its stream ended; a recovered import never observed it.
        if (captureCompletion is { } completion
            && (captureOutcome != VerificationOutputCaptureOutcome.CapturedWithKnownTruncation || !Enum.IsDefined(completion)))
        {
            throw new ArgumentException("Only a same-host capture may record a defined capture completion.", nameof(captureCompletion));
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
            CaptureCompletion = captureCompletion,
        };
    }

    public Guid Id { get; private set; }

    public Guid VerificationExecutionId { get; private set; }

    public VerificationOutputPurpose Purpose { get; private set; }

    /// <summary>ADR-0072: how the captured stream ended, separately from truncation; anything other than
    /// <see cref="OutputCaptureCompletion.Complete"/> classifies this output as incomplete. <see langword="null"/> means not recorded
    /// (every earlier output and every recovered import); it is never read as complete.</summary>
    public OutputCaptureCompletion? CaptureCompletion { get; private set; }

    public string RelativeStoragePath { get; private set; } = string.Empty;

    public string ContentHash { get; private set; } = string.Empty;

    public long ByteLength { get; private set; }

    public bool? Truncated { get; private set; }

    public VerificationOutputCaptureOutcome CaptureOutcome { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
