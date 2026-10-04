namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// One fixed root file. <see cref="Text"/>, <see cref="SizeBytes"/> and <see cref="Sha256"/> describe the complete
/// raw bytes only when <see cref="Status"/> is <see cref="GitWorkspaceInstructionStatus.Complete"/>: they were read
/// from a regular, non-link file whose open handle's final path is exactly the approved worktree root plus the
/// fixed name, are at most <see cref="GitWorkspaceInstructionContext.MaxSourceBytes"/> bytes of valid UTF-8 without
/// NUL, and hash to the raw-content identity Git reports for the same path. An omitted file may still carry the
/// length and SHA-256 that were actually established (never text); a file that was not read carries neither.
/// </summary>
public sealed record GitWorkspaceInstructionFile(
    string FileName,
    GitWorkspaceInstructionStatus Status,
    GitWorkspaceInstructionOmission? Omission,
    long? SizeBytes,
    string? Sha256,
    string? Text);
