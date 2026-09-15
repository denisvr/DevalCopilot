namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// A <see cref="GitWorkspace"/>'s lifecycle. <see cref="FailedToPrepare"/>,
/// <see cref="MissingExternally"/>, and <see cref="AlteredExternally"/> are terminal — that
/// workspace is never repaired or reused; a fresh preparation request always creates a new one.
/// <see cref="NeedsAttention"/> is not terminal: ownership is not disproven, only content trust,
/// and the workspace remains the project's current one. See ADR-0008.
/// </summary>
public enum WorkspaceStatus
{
    Preparing = 0,
    Ready = 1,
    FailedToPrepare = 2,
    MissingExternally = 3,
    AlteredExternally = 4,
    NeedsAttention = 5,
}
