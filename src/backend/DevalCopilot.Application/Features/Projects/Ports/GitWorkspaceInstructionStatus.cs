namespace DevalCopilot.Application.Features.Projects.Ports;

public enum GitWorkspaceInstructionStatus
{
    /// <summary>The complete file is provided, byte for byte, as <see cref="GitWorkspaceInstructionFile.Text"/>.</summary>
    Complete,

    /// <summary>The path was proven not to exist in the worktree at observation.</summary>
    Absent,

    /// <summary>The file was not provided, for the fixed <see cref="GitWorkspaceInstructionOmission"/>.</summary>
    Omitted,
}
