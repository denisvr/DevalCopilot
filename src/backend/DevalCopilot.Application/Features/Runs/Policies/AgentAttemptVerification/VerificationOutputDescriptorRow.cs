namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>The stored metadata of one allowlisted output row, classified by the database: <see cref="PurposeCode"/> is 1 for standard
/// output and 2 for standard error (other purposes are never selected); <see cref="CaptureCode"/> is 1 for a known-truncation capture, 2
/// for a recovered one and 0 for anything else; <see cref="TruncatedCode"/> is 0 for null, 1 for true, 2 for false and 99 for any other
/// stored value; <see cref="ByteLength"/> is null unless the stored length is an exact nonnegative safe integer. No path or hash.</summary>
internal sealed record VerificationOutputDescriptorRow(
    Guid Id, Guid ExecutionId, int PurposeCode, long? ByteLength, int CaptureCode, int TruncatedCode);
