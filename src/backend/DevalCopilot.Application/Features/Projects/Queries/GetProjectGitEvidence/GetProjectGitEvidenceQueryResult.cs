namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitEvidence;

public sealed record GetProjectGitEvidenceQueryResult(
    Guid? CheckpointId,
    int? CheckpointNumber,
    DateTimeOffset? CapturedAtUtc,
    string? HeadCommitSha,
    string? FingerprintSha256,
    int ChangedFileCount);
