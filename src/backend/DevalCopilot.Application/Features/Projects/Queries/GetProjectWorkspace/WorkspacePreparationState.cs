namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectWorkspace;

/// <summary>The truthful, user-facing projection of a project's current
/// <see cref="DevalCopilot.Domain.Features.Projects.GitWorkspace"/> — never invented beyond
/// what is actually persisted.</summary>
public enum WorkspacePreparationState
{
    NotRequested,
    Preparing,
    Ready,
    Blocked,
    NeedsAttention,
}
