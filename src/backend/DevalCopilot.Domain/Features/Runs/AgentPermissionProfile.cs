namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The closed set of permission profiles an agent assignment may claim. Unknown is
/// retained for historical attempts whose assignment columns did not yet exist.</summary>
public enum AgentPermissionProfile
{
    Unknown = 0,

    /// <summary>Read and bounded workspace-edit capability; shell, network, and MCP actions are
    /// not part of the assignment.</summary>
    WorkspaceEditOnly = 1,

    /// <summary>The configured Codex CLI <c>--sandbox read-only</c> policy governing
    /// model-generated commands for this assignment — workspace mutation is not part of the
    /// configured intent. This states what the adapter is configured to pass, never a proven or
    /// observed effective isolation boundary, and never that shell, network, or MCP actions are
    /// absent.</summary>
    ReadOnly = 2,
}
