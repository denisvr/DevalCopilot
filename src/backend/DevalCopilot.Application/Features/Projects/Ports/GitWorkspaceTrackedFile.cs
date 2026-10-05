namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// The attested before/after text of one tracked changed path from a single new Agent-context or checkpoint-inspection capture. When
/// <see cref="Omission"/> is null, <see cref="BeforeText"/> is the exact UTF-8 text of the captured HEAD's regular blob (null: the
/// path is not in HEAD) and <see cref="AfterText"/> the exact text of the current worktree file (null: the file is proven absent
/// under an owned parent). Both were read as raw bytes, are valid UTF-8 without NUL bytes, and are within the source bounds;
/// the current bytes were read from a held handle physically proven to be this single-name regular file beneath the owned
/// worktree. Terminators are preserved exactly. It is repository content: untrusted evidence that is never persisted, logged, or
/// returned through an API as such — only sealed into an Agent manifest or, as a host comparison, shown by the authenticated human
/// checkpoint inspection (ADR-0027). A consumer re-derives what it may deliver and never
/// treats missing or inconsistent facts as a raw patch.
/// </summary>
public sealed record GitWorkspaceTrackedFile(
    string Path,
    GitWorkspaceTrackedOmission? Omission,
    string? BeforeText,
    string? AfterText)
{
    /// <summary>Largest UTF-8 byte count of one source (either side).</summary>
    public const int MaxSourceBytes = 256 * 1024;

    /// <summary>Most lines of one source (either side).</summary>
    public const int MaxSourceLines = 8192;

    /// <summary>Most source bytes retained by one observation, both sides of every file together.</summary>
    public const int MaxRetainedBytes = 512 * 1024;

    public static GitWorkspaceTrackedFile Omitted(string path, GitWorkspaceTrackedOmission omission) =>
        new(path, omission, null, null);
}
