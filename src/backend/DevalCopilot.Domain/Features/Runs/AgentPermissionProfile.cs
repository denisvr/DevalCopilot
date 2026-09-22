namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The closed set of permission profiles an agent assignment may claim. Unknown is
/// retained for historical attempts whose assignment columns did not yet exist.</summary>
public enum AgentPermissionProfile
{
    Unknown = 0,

    /// <summary>Read and bounded workspace-edit capability; shell, network, and MCP actions are
    /// not part of the assignment.</summary>
    WorkspaceEditOnly = 1,
}
