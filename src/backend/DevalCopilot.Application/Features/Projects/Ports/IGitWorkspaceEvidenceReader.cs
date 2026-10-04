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
/// fingerprint either. <see cref="TrackedFiles"/> is non-null only for a new Agent-context capture, whose
/// <see cref="CompleteDiff"/> is then deliberately null: it carries the attested tracked source facts instead of the raw
/// working-path patch, accounts every tracked changed path once, and never changes the fingerprint. Whatever collection is handed
/// in, at construction or by a <c>with</c> replacement, is copied into an owned immutable <see cref="GitWorkspaceTrackedFiles"/>
/// snapshot: the facts cannot be replaced after the reader's physical proof, through the caller's collection or the returned one.</summary>
public sealed record GitWorkspaceEvidenceResult(
    GitWorkspaceEvidenceOutcome Outcome,
    string? HeadCommitSha,
    string? FingerprintSha256,
    IReadOnlyList<GitWorkspaceChangedPath> ChangedPaths,
    string? CompleteDiff,
    IReadOnlyList<GitWorkspaceUntrackedFile>? UntrackedFiles = null,
    GitWorkspaceInstructionContext? InstructionContext = null,
    IReadOnlyList<GitWorkspaceTrackedFile>? TrackedFiles = null)
{
    private readonly GitWorkspaceTrackedFiles? trackedFiles = GitWorkspaceTrackedFiles.From(TrackedFiles);

    /// <summary>The owned immutable snapshot of the attested facts (see the type summary); never the caller's collection.</summary>
    public IReadOnlyList<GitWorkspaceTrackedFile>? TrackedFiles
    {
        get => trackedFiles;
        init => trackedFiles = GitWorkspaceTrackedFiles.From(value);
    }
}

/// <summary>Status values are Git porcelain's literal one-character index and work-tree
/// columns. They remain data only; callers must not interpret them as filesystem paths.</summary>
public sealed record GitWorkspaceChangedPath(
    string Path, string? PreviousPath, string IndexStatus, string WorkTreeStatus);
