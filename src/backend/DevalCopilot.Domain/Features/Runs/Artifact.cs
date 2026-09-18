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
        bool? truncated,
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

        // A known capture outcome always carries a known true/false — never left ambiguous.
        // A host-interrupted recovery's truncation was never actually observed and must never
        // be reported as a known `false` just because the caller didn't have a real value handy.
        if (captureOutcome == ArtifactCaptureOutcome.Captured && truncated is null)
        {
            throw new ArgumentException(
                "A Captured artifact must record a known truncation value.", nameof(truncated));
        }

        if (captureOutcome == ArtifactCaptureOutcome.PartialHostInterrupted && truncated is not null)
        {
            throw new ArgumentException(
                "A PartialHostInterrupted artifact's truncation was never actually observed and must be recorded as unknown (null).",
                nameof(truncated));
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

    /// <summary><see langword="null"/> means truncation is genuinely unknown — only true for an
    /// artifact imported from an interrupted host session's partial capture, where whether the
    /// original capture was ever truncated before the host stopped writing was never observed.
    /// A known capture (<see cref="ArtifactCaptureOutcome.Captured"/>) always carries a definite
    /// <see langword="true"/>/<see langword="false"/> here, never <see langword="null"/>.</summary>
    public bool? Truncated { get; private set; }

    public ArtifactCaptureOutcome CaptureOutcome { get; private set; }

    public ArtifactSensitivity Sensitivity { get; private set; }

    public ArtifactRetentionPolicy RetentionPolicy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
