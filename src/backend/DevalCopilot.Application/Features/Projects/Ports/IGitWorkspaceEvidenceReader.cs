namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>Reads bounded, hardened Git evidence from a tool-owned workspace. It never writes
/// to Git, persists source text, invokes a shell, or trusts repository-provided helpers.</summary>
public interface IGitWorkspaceEvidenceReader
{
    Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>The same capture, additionally returning bounded, identity-verified text previews of
    /// eligible untracked files in <see cref="GitWorkspaceEvidenceResult.UntrackedFiles"/>. Only Agent
    /// context assembly asks for them; an implementation without preview support returns the plain capture.</summary>
    Task<GitWorkspaceEvidenceResult> CaptureWithUntrackedPreviewsAsync(
        string workspacePath, CancellationToken cancellationToken) => CaptureAsync(workspacePath, cancellationToken);

    /// <summary>The capture an Agent claim seals into its context manifest: the plain capture (or, when
    /// <paramref name="includeUntrackedPreviews"/> is set, the one with untracked previews) plus, in
    /// <see cref="GitWorkspaceEvidenceResult.InstructionContext"/>, the exact root instruction files observed inside
    /// the same coherent capture. An implementation without instruction support returns the corresponding capture with
    /// no instruction context, which callers must report as not captured, never as absent.</summary>
    Task<GitWorkspaceEvidenceResult> CaptureForAgentContextAsync(
        string workspacePath, bool includeUntrackedPreviews, CancellationToken cancellationToken) =>
        includeUntrackedPreviews
            ? CaptureWithUntrackedPreviewsAsync(workspacePath, cancellationToken)
            : CaptureAsync(workspacePath, cancellationToken);
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

/// <summary><see cref="UntrackedFiles"/> is empty unless previews were requested; it never changes
/// the fingerprint, which is computed from raw-content hashes and not from previews.
/// <see cref="InstructionContext"/> is null unless Agent context assembly asked for it; it never changes the
/// fingerprint either.</summary>
public sealed record GitWorkspaceEvidenceResult(
    GitWorkspaceEvidenceOutcome Outcome,
    string? HeadCommitSha,
    string? FingerprintSha256,
    IReadOnlyList<GitWorkspaceChangedPath> ChangedPaths,
    string? CompleteDiff,
    IReadOnlyList<GitWorkspaceUntrackedFile>? UntrackedFiles = null,
    GitWorkspaceInstructionContext? InstructionContext = null);

/// <summary>Status values are Git porcelain's literal one-character index and work-tree
/// columns. They remain data only; callers must not interpret them as filesystem paths.</summary>
public sealed record GitWorkspaceChangedPath(
    string Path, string? PreviousPath, string IndexStatus, string WorkTreeStatus);
