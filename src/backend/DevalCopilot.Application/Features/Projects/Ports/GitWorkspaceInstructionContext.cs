namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// The exact root <c>AGENTS.md</c> and <c>CLAUDE.md</c> of one worktree as observed inside a single coherent Git
/// capture, always both entries in <see cref="FileNames"/> order. It is repository content: untrusted evidence that
/// is never persisted, logged, or returned through an API — only sealed into an Agent manifest. Nothing outside
/// those two root paths is ever read: no import, Markdown reference, nested or parent file, link, home path, or
/// setting is followed.
/// </summary>
public sealed record GitWorkspaceInstructionContext(IReadOnlyList<GitWorkspaceInstructionFile> Files)
{
    /// <summary>Largest raw UTF-8 content of one source that may be admitted as complete text.</summary>
    public const int MaxSourceBytes = 8 * 1024;

    /// <summary>The only paths ever read, in the fixed order they are reported and fitted.</summary>
    public static IReadOnlyList<string> FileNames { get; } = ["AGENTS.md", "CLAUDE.md"];

    /// <summary>Whether a Git-reported path is one of the two root instruction names. They are reserved to the controlled
    /// instruction section: new Agent delivery never carries their text as a generic untracked preview or tracked diff, whatever
    /// their instruction status. Compared ignoring case because the Windows file system does; only the root path matches.</summary>
    public static bool IsReservedPath(string? path) =>
        path is not null && FileNames.Any(name => string.Equals(name, path, StringComparison.OrdinalIgnoreCase));
}
