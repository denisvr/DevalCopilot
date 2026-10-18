using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>
/// Admits the metadata of one allowlisted output stream identically for the inspection and the window (ADR-0052). No row is
/// <see cref="RecordedVerificationOutputDescriptorStatus.NotRecorded"/>; more than one row, an empty identity, a rejected length, an
/// unknown capture outcome or a capture/truncation pair the Domain never records is
/// <see cref="RecordedVerificationOutputDescriptorStatus.Unavailable"/>. The window additionally admits the stored path and hash it
/// passes, unchanged, to the artifact store's own proof.
/// </summary>
internal static class RecordedVerificationOutputAdmission
{
    public const int StandardOutputCode = 1;
    public const int StandardErrorCode = 2;

    private const int MaximumRelativeStoragePathLength = 1024;
    private const string ContentHashPrefix = "sha256:";
    private const int ContentHashDigits = 64;

    public static int PurposeCode(VerificationOutputPurpose purpose) =>
        purpose == VerificationOutputPurpose.StandardOutput ? StandardOutputCode : StandardErrorCode;

    public static RecordedVerificationOutputDescriptor Admit(IReadOnlyList<VerificationOutputDescriptorRow> rows)
    {
        if (rows.Count == 0)
        {
            return RecordedVerificationOutputDescriptor.NotRecorded;
        }

        if (rows.Count > 1)
        {
            return RecordedVerificationOutputDescriptor.Unavailable;
        }

        var row = rows[0];
        if (row.Id == Guid.Empty || row.ByteLength is not { } byteLength)
        {
            return RecordedVerificationOutputDescriptor.Unavailable;
        }

        return (row.CaptureCode, row.TruncatedCode) switch
        {
            (1, 1) => Recorded(row.Id, byteLength, VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, true),
            (1, 2) => Recorded(row.Id, byteLength, VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, false),
            (2, 0) => Recorded(row.Id, byteLength, VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption, null),
            _ => RecordedVerificationOutputDescriptor.Unavailable,
        };
    }

    /// <summary>Whether the guarded storage carries a nonblank, relative path of at most 1024 UTF-16 units and the store's exact content
    /// hash form (sha256: followed by 64 lowercase hexadecimal digits). Neither value is altered.</summary>
    public static bool IsAdmissibleStorage(VerificationOutputStorageRow row) =>
        !row.RelativeStoragePathOversized
        && !row.ContentHashOversized
        && !string.IsNullOrWhiteSpace(row.RelativeStoragePath)
        && row.RelativeStoragePath.Length <= MaximumRelativeStoragePathLength
        && !Path.IsPathRooted(row.RelativeStoragePath)
        && IsContentHash(row.ContentHash);

    private static bool IsContentHash(string? value) =>
        value is { Length: 71 }
        && value.StartsWith(ContentHashPrefix, StringComparison.Ordinal)
        && value.AsSpan(ContentHashPrefix.Length).Length == ContentHashDigits
        && value.AsSpan(ContentHashPrefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static RecordedVerificationOutputDescriptor Recorded(
        Guid artifactId, long byteLength, VerificationOutputCaptureOutcome captureOutcome, bool? truncated) =>
        new(RecordedVerificationOutputDescriptorStatus.Recorded, artifactId, byteLength, captureOutcome, truncated);
}
