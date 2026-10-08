namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>Every input the host derived; the caller of the public operation supplied none of these paths, refs or SHAs.</summary>
public sealed record LocalCommitPreparationRequest(
    Guid OperationId,
    string MainRepositoryPath,
    string WorkspacePath,
    string BranchName,
    string ExpectedParentCommitSha,
    string CheckpointFingerprintSha256,
    IReadOnlyList<LocalCommitChangedPath> ChangedPaths,
    string NormalizedMessage,
    LocalCommitOwnership Ownership,
    DateTimeOffset NowUtc);
