namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>The persisted, immutable operation facts the executor and recovery prove against Git, never inputs to trust.</summary>
public sealed record LocalCommitFacts(
    Guid OperationId,
    string MainRepositoryPath,
    string WorkspacePath,
    string BranchName,
    string ParentCommitSha,
    string TreeSha,
    string CommitSha,
    string CommitMessage,
    string AuthorName,
    string AuthorEmail,
    long CommitTimeUnixSeconds,
    string IndexPreimageSha256,
    string PreparedIndexSha256,
    string PreparedIndexRelativePath,
    LocalCommitOwnership Ownership);
