namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>Reads bounded, hardened Git evidence from a tool-owned workspace. It never writes
/// to Git, persists source text, invokes a shell, or trusts repository-provided helpers.</summary>
public interface IGitWorkspaceEvidenceReader
{
    Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken);
}

public enum GitWorkspaceEvidenceOutcome
{
    Success,
    GitUnavailable,
    GitInvocationFailed,
    GitInvocationTimedOut,
    EvidenceTooLarge,
    RepositoryChangedDuringCapture,
    InvalidGitState,
}

public sealed record GitWorkspaceEvidenceResult(
    GitWorkspaceEvidenceOutcome Outcome,
    string? HeadCommitSha,
    string? FingerprintSha256,
    IReadOnlyList<GitWorkspaceChangedPath> ChangedPaths,
    string? CompleteDiff);

/// <summary>Status values are Git porcelain's literal one-character index and work-tree
/// columns. They remain data only; callers must not interpret them as filesystem paths.</summary>
public sealed record GitWorkspaceChangedPath(
    string Path, string? PreviousPath, string IndexStatus, string WorkTreeStatus);
