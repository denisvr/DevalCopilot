namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>One requested output row for a sealed window: the same classified metadata as <see cref="VerificationOutputDescriptorRow"/>
/// plus the stored relative path and content hash, each read only within its byte guard and flagged when oversized. Path and hash are
/// used only for the artifact store's own proof and never leave the host.</summary>
internal sealed record VerificationOutputStorageRow(
    Guid Id,
    Guid ExecutionId,
    int PurposeCode,
    long? ByteLength,
    int CaptureCode,
    int TruncatedCode,
    string? RelativeStoragePath,
    bool RelativeStoragePathOversized,
    string? ContentHash,
    bool ContentHashOversized)
{
    public VerificationOutputDescriptorRow Descriptor => new(Id, ExecutionId, PurposeCode, ByteLength, CaptureCode, TruncatedCode);
}
