namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One immutable durable fact about captured output: hash, size, truncation, and where it lives
/// beneath the application-owned artifact root. Never mutated after creation — a later capture
/// for the same attempt and purpose is a distinct concern the unique (AttemptId, Purpose)
/// constraint prevents from ever being represented twice, not a reason to update this row.
/// </summary>
public sealed class Artifact
{
    private Artifact()
    {
    }

    public static Artifact Record(
        Guid id,
        Guid runId,
        Guid attemptId,
        ArtifactPurpose purpose,
        string mediaType,
        string relativeStoragePath,
        string contentHash,
        long byteLength,
        bool truncated,
        ArtifactCaptureOutcome captureOutcome,
        ArtifactSensitivity sensitivity,
        ArtifactRetentionPolicy retentionPolicy,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeStoragePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Must not be negative.");
        }

        return new Artifact
        {
            Id = id,
            RunId = runId,
            AttemptId = attemptId,
            Purpose = purpose,
            MediaType = mediaType,
            RelativeStoragePath = relativeStoragePath,
            ContentHash = contentHash,
            ByteLength = byteLength,
            Truncated = truncated,
            CaptureOutcome = captureOutcome,
            Sensitivity = sensitivity,
            RetentionPolicy = retentionPolicy,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid AttemptId { get; private set; }

    public ArtifactPurpose Purpose { get; private set; }

    public string MediaType { get; private set; } = string.Empty;

    /// <summary>Relative to the application-owned artifact root. Never resolved without a
    /// canonical containment check — see <c>IArtifactStore</c>.</summary>
    public string RelativeStoragePath { get; private set; } = string.Empty;

    /// <summary>"sha256:&lt;hex&gt;" over the exact sealed file contents.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    public long ByteLength { get; private set; }

    public bool Truncated { get; private set; }

    public ArtifactCaptureOutcome CaptureOutcome { get; private set; }

    public ArtifactSensitivity Sensitivity { get; private set; }

    public ArtifactRetentionPolicy RetentionPolicy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
