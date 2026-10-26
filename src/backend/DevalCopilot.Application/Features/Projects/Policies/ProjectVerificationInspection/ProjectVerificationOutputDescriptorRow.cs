namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>The stored metadata of one allowlisted output row of the selected execution (ADR-0069), classified by the database. The
/// artifact and execution identities are raw stored bytes within their bound; <see cref="PurposeCode"/> is 1 for standard output and 2 for
/// standard error (other purposes are never selected); <see cref="CaptureCode"/> is 1 for a known-truncation capture, 2 for a recovered
/// one and 0 for anything else; the length is null unless the stored value is an exact nonnegative safe integer. No path or hash.</summary>
internal sealed record ProjectVerificationOutputDescriptorRow(
    byte[]? Id, byte[]? ExecutionId, int PurposeCode, long? ByteLength, int CaptureCode, byte[]? TruncatedStored, bool TruncatedPresent);
