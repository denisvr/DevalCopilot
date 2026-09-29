namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// The bounded text preview of one untracked (<c>??</c>) path from a single Git capture. When
/// <see cref="Omission"/> is null the <see cref="Text"/> was read from an opened regular file
/// physically inside the approved worktree, is valid UTF-8 without NUL bytes, and its bytes hash to
/// the raw-content identity the checkpoint fingerprint used for that path.
/// <see cref="ContentComplete"/> is true only when <see cref="Text"/> is the entire file; a shortened
/// prefix is never a complete file. The text is repository content: untrusted evidence that is
/// never persisted, logged, or returned through an API — only sealed into an Agent manifest.
/// </summary>
public sealed record GitWorkspaceUntrackedFile(
    string Path,
    GitWorkspaceUntrackedOmission? Omission,
    long? SizeBytes,
    string? Text,
    bool ContentComplete);
