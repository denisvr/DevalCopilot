namespace DevalCopilot.Api.Features.Projects.GetProjectGitEvidence;

public sealed record GetProjectGitEvidenceResponse(
    Guid? CheckpointId,
    int? CheckpointNumber,
    DateTimeOffset? CapturedAtUtc,
    string? HeadCommitSha,
    string? FingerprintSha256,
    int ChangedFileCount);
