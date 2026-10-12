namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;

/// <summary>
/// The closed state of the explicit repository-local GitHub origin observation (ADR-0039). Only
/// <see cref="NotConfigured"/> and <see cref="Available"/> are observations and carry a time; <see cref="Unavailable"/>
/// covers every refusal and every unusable outcome without saying which.
/// </summary>
public enum GitHubOriginStatus
{
    Unavailable = 0,
    NotConfigured = 1,
    Available = 2,
}
