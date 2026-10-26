namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>The requested output row of one window (ADR-0069): the same classified metadata as
/// <see cref="ProjectVerificationOutputDescriptorRow"/> plus the stored relative path and content hash, each read only within its byte
/// guard and flagged when oversized. Path and hash are used only for the artifact store's own proof and never leave the host.</summary>
internal sealed record ProjectVerificationOutputStorageRow(
    byte[]? Id,
    byte[]? ExecutionId,
    int PurposeCode,
    long? ByteLength,
    int CaptureCode,
    byte[]? TruncatedStored,
    bool TruncatedPresent,
    string? RelativeStoragePath,
    bool RelativeStoragePathOversized,
    string? ContentHash,
    bool ContentHashOversized);
